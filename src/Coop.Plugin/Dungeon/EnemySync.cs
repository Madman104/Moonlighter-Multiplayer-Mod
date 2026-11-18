using System;
using System.Collections.Generic;
using Coop.Core.Dungeon;
using Coop.Core.Protocol;
using Coop.Core.Session;
using Coop.Plugin.Runtime;
using HarmonyLib;
using UnityEngine;

namespace Coop.Plugin.Dungeon
{
    /// <summary>Stable per-room enemy number, given in spawn order (same on both games).</summary>
    internal sealed class CoopEnemyId : MonoBehaviour
    {
        public int Id;
    }

    /// <summary>
    /// Stage 3a: enemy health and deaths are shared; each game runs its own enemy AI.
    /// Enemies have double health in co-op, both games pick the same champions. Loot is per
    /// player only for quest drops: enemy loot is rolled by the host and shared (FloorItems).
    /// Enemies are identified by a spawn-order id, not by their place in room.roomEnemies
    /// (that list shrinks on death and AI adds/removes enemies at local timing).
    /// </summary>
    internal static class EnemySync
    {
        public const float HealthBroadcastInterval = 1f;

        private sealed class SeedState { public bool Seeded; public UnityEngine.Random.State Saved; }

        private static bool _applyingRemote;
        private static float _nextBroadcast;
        private static readonly Dictionary<DungeonRoom, int> NextId = new Dictionary<DungeonRoom, int>();

        public static void Apply(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(EnemyStats), nameof(EnemyStats.Init), new[] { typeof(string) }),
                postfix: new HarmonyMethod(typeof(EnemySync), nameof(InitPostfix)));
            harmony.Patch(AccessTools.Method(typeof(DungeonRoom), nameof(DungeonRoom.SpawnEnemies)),
                prefix: new HarmonyMethod(typeof(EnemySync), nameof(SpawnPrefix)),
                postfix: new HarmonyMethod(typeof(EnemySync), nameof(SpawnPostfix)));
            harmony.Patch(AccessTools.Method(typeof(DungeonRoom), nameof(DungeonRoom.SpawnEnemy)),
                postfix: new HarmonyMethod(typeof(EnemySync), nameof(SpawnOnePostfix)));
            harmony.Patch(AccessTools.PropertySetter(typeof(EnemyStats), nameof(EnemyStats.CurrentHealth)),
                prefix: new HarmonyMethod(typeof(EnemySync), nameof(HealthPrefix)),
                postfix: new HarmonyMethod(typeof(EnemySync), nameof(HealthPostfix)));
        }

        public static void Attach(CoopLink session)
        {
            session.GameMessageReceived += OnGameMessage;
        }

        private static bool Connected(out CoopLink s)
        {
            s = CoopManager.Instance != null ? CoopManager.Instance.Session : null;
            return s != null && s.State == SessionState.Connected;
        }

        private static bool InSharedDungeon() => SharedCultures.InSharedDungeon();

        // ---- double health ----

        private static void InitPostfix(EnemyStats __instance)
        {
            if (!Connected(out _) || !InSharedDungeon()) return;
            var max = Traverse.Create(__instance).Field("_currentStats").Field("maxHealth");
            max.SetValue(max.GetValue<float>() * EnemyHealthRules.CoopMaxHealthFactor);
            _applyingRemote = true; // not a hit: don't send
            try { __instance.CurrentHealth = __instance.maxHealth; }
            finally { _applyingRemote = false; }
        }

        // ---- same champion picks; ids in spawn order ----

        private static int _roomSpawnDepth;

        private static void SpawnPrefix(DungeonRoom __instance, out SeedState __state)
        {
            _roomSpawnDepth++;
            __state = new SeedState();
            NextId[__instance] = 0;
            if (!Connected(out _) || !InSharedDungeon()) return;
            var dm = DungeonManager.Instance;
            __state.Saved = UnityEngine.Random.state;
            __state.Seeded = true;
            UnityEngine.Random.InitState(RoomSeed.For((int)dm.culture, dm.level, __instance.x, __instance.y, __instance.isSecret));
        }

        private static void SpawnPostfix(SeedState __state)
        {
            if (_roomSpawnDepth > 0) _roomSpawnDepth--;
            if (__state != null && __state.Seeded) UnityEngine.Random.state = __state.Saved;
        }

        private static void SpawnOnePostfix(DungeonRoom __instance, Enemy __result, DungeonRoomInfo.EnemySpawn spawnPair, int tile)
        {
            if (__result == null || __result.GetComponent<CoopEnemyId>() != null) return;
            // Spawned by an enemy's AI (not the room filling itself): a shared summon.
            if (_roomSpawnDepth == 0 && SummonSync.OnSpawned(__instance, __result, spawnPair, tile)) return;
            NextId.TryGetValue(__instance, out int id);
            NextId[__instance] = id + 1;
            __result.gameObject.AddComponent<CoopEnemyId>().Id = id;
        }

        private static int IdOf(EnemyStats stats, out DungeonRoom room)
        {
            room = null;
            var enemy = stats.GetComponentInParent<Enemy>();
            if (enemy == null) return -1;
            var tag = enemy.GetComponent<CoopEnemyId>();
            room = enemy.enemyRoom;
            return tag != null && room != null && tag.Id <= 255 ? tag.Id : -1;
        }

        // ---- shared damage (id captured before the setter: a kill removes the enemy from roomEnemies) ----

        private static void HealthPrefix(EnemyStats __instance, out float __state)
        {
            __state = __instance.CurrentHealth;
        }

        private static void HealthPostfix(EnemyStats __instance, float __state)
        {
            if (_applyingRemote || !Connected(out var s) || !InSharedDungeon()) return;
            float delta = __state - __instance.CurrentHealth;
            if (delta <= 0f) return;
            int id = IdOf(__instance, out var room);
            if (id < 0) return;
            s.SendGame(MessageType.EnemyDamage, new EnemyDamage
            {
                X = (short)room.x, Y = (short)room.y, Secret = room.isSecret, Index = (byte)id, Delta = delta,
            });
        }

        // ---- receiving ----

        private static void OnGameMessage(MessageType type, object msg)
        {
            if (type == MessageType.EnemyDamage) ApplyDamage((EnemyDamage)msg);
            else if (type == MessageType.EnemyHealth) ApplyHealth((EnemyHealth)msg);
        }

        private static readonly Dictionary<long, float> PendingDamage = new Dictionary<long, float>();
        /// <summary>Enemies whose death here must not roll loot again.</summary>
        public static readonly HashSet<Enemy> NoLoot = new HashSet<Enemy>();
        private static DungeonRoom _pendingCheckedRoom;
        private static int _pendingScene = int.MinValue;

        private static long PendingKey(int x, int y, bool secret, int id) => ((long)EnemyOwnership.RoomKey(x, y, secret) << 8) | (uint)(id & 0xFF);

        /// <summary>Applies hits other players made in a room once this player walks into it.</summary>
        private static void ApplyPendingDamage()
        {
            var dm = DungeonManager.Instance;
            int scene = GameInfo.CurrentSceneId();
            if (scene != _pendingScene) { _pendingScene = scene; PendingDamage.Clear(); NoLoot.Clear(); _pendingCheckedRoom = null; return; }
            var room = dm != null ? dm.currentDungeonRoom : null;
            if (room == null || room == _pendingCheckedRoom || PendingDamage.Count == 0 || FloorSync.ClientBuilding) { _pendingCheckedRoom = room; return; }
            _pendingCheckedRoom = room;
            int key = EnemyOwnership.RoomKey(room.x, room.y, room.isSecret);
            var done = new List<long>();
            foreach (var kv in PendingDamage)
            {
                if ((int)(kv.Key >> 8) != key) continue;
                done.Add(kv.Key);
                var stats = FindEnemy(room, (int)(kv.Key & 0xFF));
                if (stats == null || !stats.isAlive) continue;
                var enemy = stats.GetComponentInParent<Enemy>();
                if (enemy != null) NoLoot.Add(enemy); // killed by someone else earlier: their game rolled the loot
                SetHealth(stats, stats.CurrentHealth - kv.Value);
            }
            foreach (var k in done) PendingDamage.Remove(k);
        }

        private static void ApplyDamage(EnemyDamage d)
        {
            var room = FindRoom(d.X, d.Y, d.Secret);
            if (room != null && !LateApply.IsCurrent(room))
            {
                long k = PendingKey(d.X, d.Y, d.Secret, d.Index);
                PendingDamage.TryGetValue(k, out float sum);
                PendingDamage[k] = sum + d.Delta;
                return;
            }
            var stats = FindEnemy(room, d.Index);
            if (stats == null || !stats.isAlive) return;
            SetHealth(stats, stats.CurrentHealth - d.Delta);
        }

        private static void ApplyHealth(EnemyHealth h)
        {
            if (!Connected(out var s) || s.IsHost) return;
            var room = FindRoom(h.X, h.Y, h.Secret);
            if (room == null || !LateApply.IsCurrent(room)) return; // the host's room isn't ours
            for (int id = 0; id < h.Hp.Length; id++)
            {
                if (h.Hp[id] < 0f) continue; // host has no enemy with this id
                var stats = FindEnemy(room, id);
                if (stats == null || !stats.isAlive) continue;
                if (h.Max != null && id < h.Max.Length && h.Max[id] > 0f && Math.Abs(stats.maxHealth - h.Max[id]) > 0.5f)
                {
                    // Same max health as the host (difficulty and NG+ can differ per player).
                    try { Traverse.Create(stats).Field("_currentStats").Field("maxHealth").SetValue(h.Max[id]); }
                    catch (Exception e) { CoopPlugin.Log.LogWarning("Could not match enemy max health: " + e.Message); }
                }
                if (EnemyHealthRules.NeedsCorrection(stats.CurrentHealth, h.Hp[id]))
                    SetHealth(stats, h.Hp[id]);
            }
        }

        internal static void SetHealth(EnemyStats stats, float value)
        {
            // Invincibility windows run on each game's own AI; the other player's hit still counts.
            var inv = Traverse.Create(stats).Field("_invincible");
            bool wasInvincible = inv.GetValue<bool>();
            _applyingRemote = true;
            try
            {
                if (wasInvincible) inv.SetValue(false);
                stats.CurrentHealth = Mathf.Max(0f, value);
            }
            catch (Exception e) { CoopPlugin.Log.LogWarning("Could not apply enemy health: " + e.Message); }
            finally
            {
                if (wasInvincible && stats != null && stats.isAlive) inv.SetValue(true);
                _applyingRemote = false;
            }
        }

        /// <summary>Host: send the current room's enemy health (indexed by id) once a second.</summary>
        public static void Tick()
        {
            if (Connected(out _) && InSharedDungeon()) ApplyPendingDamage();
            if (Time.realtimeSinceStartup < _nextBroadcast) return;
            _nextBroadcast = Time.realtimeSinceStartup + HealthBroadcastInterval;
            if (!Connected(out var s) || !s.IsHost || !InSharedDungeon()) return;
            var room = DungeonManager.Instance.currentDungeonRoom;
            if (room == null) return;
            var tags = room.GetComponentsInChildren<CoopEnemyId>(true);
            if (tags.Length == 0) return;
            int maxId = 0;
            foreach (var t in tags) maxId = Math.Max(maxId, t.Id);
            var hp = new float[Math.Min(maxId + 1, 255)];
            var max = new float[hp.Length];
            for (int i = 0; i < hp.Length; i++) { hp[i] = -1f; max[i] = -1f; }
            foreach (var t in tags)
            {
                if (t.Id >= hp.Length) continue;
                var e = t.GetComponent<Enemy>();
                if (e != null && e.enemyRoom == room && e.enemyStats != null) { hp[t.Id] = e.enemyStats.CurrentHealth; max[t.Id] = e.enemyStats.maxHealth; }
            }
            s.SendGame(MessageType.EnemyHealth, new EnemyHealth { X = (short)room.x, Y = (short)room.y, Secret = room.isSecret, Hp = hp, Max = max });
        }

        private static DungeonRoom FindRoom(int x, int y, bool secret)
        {
            var dm = DungeonManager.Instance;
            if (dm == null) return null;
            foreach (var r in dm.dungeonRooms)
                if (r != null && r.x == x && r.y == y && r.isSecret == secret) return r;
            if (dm.bossRoom != null && dm.bossRoom.x == x && dm.bossRoom.y == y) return dm.bossRoom;
            return null;
        }

        private static EnemyStats FindEnemy(DungeonRoom room, int id)
        {
            if (room == null) return null;
            foreach (var t in room.GetComponentsInChildren<CoopEnemyId>(true))
            {
                if (t.Id != id) continue;
                var e = t.GetComponent<Enemy>();
                if (e != null && e.enemyRoom == room) return e.enemyStats;
            }
            return null;
        }
    }
}
