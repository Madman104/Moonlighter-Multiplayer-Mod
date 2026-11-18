using System;
using Coop.Core.Dungeon;
using Coop.Core.Protocol;
using Coop.Core.Session;
using Coop.Plugin.Runtime;
using HarmonyLib;
using UnityEngine;

namespace Coop.Plugin.Dungeon
{
    /// <summary>
    /// Stage 3a: enemy health and deaths are shared; each game runs its own enemy AI.
    /// Enemies have double health in co-op, both games pick the same champions, and only the
    /// host drops loot (items are shared in stage 4).
    /// </summary>
    internal static class EnemySync
    {
        public const float HealthBroadcastInterval = 1f;

        private static bool _applyingRemote;
        private static float _nextBroadcast;

        public static void Apply(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(EnemyStats), nameof(EnemyStats.Init), new[] { typeof(string) }),
                postfix: new HarmonyMethod(typeof(EnemySync), nameof(InitPostfix)));
            harmony.Patch(AccessTools.Method(typeof(DungeonRoom), nameof(DungeonRoom.SpawnEnemies)),
                prefix: new HarmonyMethod(typeof(EnemySync), nameof(SpawnPrefix)),
                postfix: new HarmonyMethod(typeof(EnemySync), nameof(SpawnPostfix)));
            harmony.Patch(AccessTools.PropertySetter(typeof(EnemyStats), nameof(EnemyStats.CurrentHealth)),
                prefix: new HarmonyMethod(typeof(EnemySync), nameof(HealthPrefix)),
                postfix: new HarmonyMethod(typeof(EnemySync), nameof(HealthPostfix)));
            harmony.Patch(AccessTools.Method(typeof(Enemy), nameof(Enemy.DropItemsOnDeath)),
                prefix: new HarmonyMethod(typeof(EnemySync), nameof(DropPrefix)));
        }

        public static void Attach(PeerSession session)
        {
            session.GameMessageReceived += OnGameMessage;
        }

        private static bool Connected(out PeerSession s)
        {
            s = CoopManager.Instance != null ? CoopManager.Instance.Session : null;
            return s != null && s.State == SessionState.Connected;
        }

        /// <summary>Co-op rules apply only inside a dungeon floor both players share.</summary>
        private static bool InSharedDungeon()
        {
            var gm = GameManager.Instance;
            var dm = DungeonManager.Instance;
            return gm != null && dm != null && gm.IsDungeonSceneLoaded()
                && dm.culture >= ItemMaster.Culture.Golem && dm.culture <= ItemMaster.Culture.Tech;
        }

        // ---- double health ----

        private static void InitPostfix(EnemyStats __instance)
        {
            if (!Connected(out _) || !InSharedDungeon()) return;
            var stats = Traverse.Create(__instance).Field("_currentStats");
            var max = stats.Field("maxHealth");
            max.SetValue(max.GetValue<float>() * EnemyHealthRules.CoopMaxHealthFactor);
            _applyingRemote = true; // not a hit: don't send
            try { __instance.CurrentHealth = __instance.maxHealth; }
            finally { _applyingRemote = false; }
        }

        // ---- same champion picks on both games ----

        private static void SpawnPrefix(DungeonRoom __instance, out UnityEngine.Random.State __state)
        {
            __state = UnityEngine.Random.state;
            if (!Connected(out _) || !InSharedDungeon()) return;
            var dm = DungeonManager.Instance;
            UnityEngine.Random.InitState(RoomSeed.For((int)dm.culture, dm.level, __instance.x, __instance.y, __instance.isSecret));
        }

        private static void SpawnPostfix(UnityEngine.Random.State __state)
        {
            UnityEngine.Random.state = __state;
        }

        // ---- shared damage ----

        private static void HealthPrefix(EnemyStats __instance, out float __state)
        {
            __state = __instance.CurrentHealth;
        }

        private static void HealthPostfix(EnemyStats __instance, float __state)
        {
            if (_applyingRemote || !Connected(out var s) || !InSharedDungeon()) return;
            float delta = __state - __instance.CurrentHealth;
            if (delta <= 0f) return;
            var enemy = __instance.GetComponentInParent<Enemy>();
            if (enemy == null || enemy.enemyRoom == null) return;
            int index = enemy.enemyRoom.roomEnemies.IndexOf(enemy);
            if (index < 0 || index > 255) return;
            var room = enemy.enemyRoom;
            s.SendGame(MessageType.EnemyDamage, new EnemyDamage
            {
                X = (short)room.x, Y = (short)room.y, Secret = room.isSecret, Index = (byte)index, Delta = delta,
            });
        }

        // ---- loot only on the host ----

        private static bool DropPrefix()
        {
            return !(Connected(out var s) && !s.IsHost && InSharedDungeon());
        }

        // ---- receiving ----

        private static void OnGameMessage(MessageType type, object msg)
        {
            if (type == MessageType.EnemyDamage) ApplyDamage((EnemyDamage)msg);
            else if (type == MessageType.EnemyHealth) ApplyHealth((EnemyHealth)msg);
        }

        private static void ApplyDamage(EnemyDamage d)
        {
            var enemy = FindEnemy(d.X, d.Y, d.Secret, d.Index);
            if (enemy == null || enemy.enemyStats == null || !enemy.enemyStats.isAlive) return;
            SetHealth(enemy.enemyStats, enemy.enemyStats.CurrentHealth - d.Delta);
        }

        private static void ApplyHealth(EnemyHealth h)
        {
            if (!Connected(out var s) || s.IsHost) return;
            var room = FindRoom(h.X, h.Y, h.Secret);
            if (room == null) return;
            for (int i = 0; i < h.Hp.Length && i < room.roomEnemies.Count; i++)
            {
                var e = room.roomEnemies[i];
                if (e == null || e.enemyStats == null || !e.enemyStats.isAlive) continue;
                if (EnemyHealthRules.NeedsCorrection(e.enemyStats.CurrentHealth, h.Hp[i]))
                    SetHealth(e.enemyStats, h.Hp[i]);
            }
        }

        private static void SetHealth(EnemyStats stats, float value)
        {
            _applyingRemote = true;
            try { stats.CurrentHealth = Mathf.Max(0f, value); }
            catch (Exception e) { CoopPlugin.Log.LogWarning("Could not apply enemy health: " + e.Message); }
            finally { _applyingRemote = false; }
        }

        /// <summary>Host: send the current room's enemy health once a second.</summary>
        public static void Tick()
        {
            if (Time.realtimeSinceStartup < _nextBroadcast) return;
            _nextBroadcast = Time.realtimeSinceStartup + HealthBroadcastInterval;
            if (!Connected(out var s) || !s.IsHost || !InSharedDungeon()) return;
            var room = DungeonManager.Instance.currentDungeonRoom;
            if (room == null || room.roomEnemies == null || room.roomEnemies.Count == 0) return;
            var hp = new float[Math.Min(room.roomEnemies.Count, 255)];
            for (int i = 0; i < hp.Length; i++)
            {
                var e = room.roomEnemies[i];
                hp[i] = e != null && e.enemyStats != null ? e.enemyStats.CurrentHealth : 0f;
            }
            s.SendGame(MessageType.EnemyHealth, new EnemyHealth { X = (short)room.x, Y = (short)room.y, Secret = room.isSecret, Hp = hp });
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

        private static Enemy FindEnemy(int x, int y, bool secret, int index)
        {
            var room = FindRoom(x, y, secret);
            if (room == null || room.roomEnemies == null || index >= room.roomEnemies.Count) return null;
            return room.roomEnemies[index];
        }
    }
}
