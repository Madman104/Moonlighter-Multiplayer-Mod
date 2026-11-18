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
    /// <summary>
    /// Enemy-wave rooms (secret rooms with a shrine that summons waves) for everyone.
    /// The player who starts a wave runs it: their game's portals spawn the enemies, which are
    /// shared like summons (SummonSync). Every other game starts the same wave as a spectator:
    /// same animation, closed room and reward chest, but its portals spawn nothing. When the
    /// runner's wave is beaten, the spectators finish theirs. Each wave's reward chest is added
    /// to the room's chests, so its content and one-at-a-time use are shared (ChestSync).
    /// </summary>
    internal static class WaveSync
    {
        private static readonly HashSet<DungeonRoom> Spectating = new HashSet<DungeonRoom>();
        private static bool _remote;
        private static readonly Dictionary<EnemyWavesShrine, Chest> LastChest = new Dictionary<EnemyWavesShrine, Chest>();

        private static readonly System.Reflection.MethodInfo StartWave = AccessTools.Method(typeof(EnemyWavesShrine), "StartWave");
        private static readonly System.Reflection.MethodInfo Finish = AccessTools.Method(typeof(EnemyWavesShrine), "CombatPhaseFinished");

        public static void Apply(Harmony harmony)
        {
            if (StartWave == null || Finish == null) { CoopPlugin.Log.LogWarning("Enemy-wave rooms not shared (game code changed)"); return; }
            harmony.Patch(StartWave, postfix: new HarmonyMethod(typeof(WaveSync), nameof(StartPostfix)));
            harmony.Patch(Finish, prefix: new HarmonyMethod(typeof(WaveSync), nameof(FinishPrefix)),
                postfix: new HarmonyMethod(typeof(WaveSync), nameof(FinishPostfix)));
            var spawn = AccessTools.Method(typeof(EnemyWarpSpawner), "SpawnEnemy");
            if (spawn != null) harmony.Patch(spawn, prefix: new HarmonyMethod(typeof(WaveSync), nameof(WarpSpawnPrefix)));
            var chest = AccessTools.Method(typeof(EnemyWavesShrine), "ChestSpawn");
            if (chest != null) harmony.Patch(chest, postfix: new HarmonyMethod(typeof(WaveSync), nameof(ChestPostfix)));
        }

        public static void Attach(CoopLink session)
        {
            Spectating.Clear();
            LastChest.Clear();
            session.StateChanged += st => { if (st == SessionState.Closed) Spectating.Clear(); };
        }

        private static bool Sharing(out CoopLink s)
        {
            s = CoopManager.Instance != null ? CoopManager.Instance.Session : null;
            return s != null && s.State == SessionState.Connected && SharedCultures.InSharedDungeon();
        }

        private static DungeonRoom RoomOf(EnemyWavesShrine shrine) => Traverse.Create(shrine).Field("myRoom").GetValue<DungeonRoom>();

        private static RoomObject Describe(DungeonRoom room, EnemyWavesShrine shrine, byte kind)
        {
            var local = shrine.transform.position - room.transform.position;
            return new RoomObject { RoomKey = EnemyOwnership.RoomKey(room.x, room.y, room.isSecret), Kind = kind, X = local.x, Y = local.y, Value = 1f };
        }

        // ---- local wave: tell the others ----

        private static void StartPostfix(EnemyWavesShrine __instance)
        {
            if (_remote || !Sharing(out var s)) return;
            var room = RoomOf(__instance);
            if (room != null) s.SendGame(MessageType.RoomObject, Describe(room, __instance, RoomObject.WaveStart));
        }

        private static void FinishPrefix(EnemyWavesShrine __instance, out bool __state)
            => __state = Traverse.Create(__instance).Field("currentStatus").GetValue<object>()?.ToString() == "Battle";

        private static void FinishPostfix(EnemyWavesShrine __instance, bool __state)
        {
            var room = RoomOf(__instance);
            if (room != null) Spectating.Remove(room);
            if (!__state || _remote || !Sharing(out var s) || room == null) return;
            s.SendGame(MessageType.RoomObject, Describe(room, __instance, RoomObject.WaveDone));
        }

        /// <summary>Spectators' portals open and close as usual but spawn nothing: the runner's enemies arrive as summons.</summary>
        private static bool WarpSpawnPrefix(EnemyWarpSpawner __instance) => __instance == null || __instance.room == null || !Spectating.Contains(__instance.room);

        private static void ChestPostfix(EnemyWavesShrine __instance)
        {
            var room = RoomOf(__instance);
            var chest = Traverse.Create(__instance).Field("_chest").GetValue<Chest>();
            if (room == null || chest == null || room.roomChests == null) return;
            // The previous tier's chest is being destroyed (not null until the frame ends).
            if (LastChest.TryGetValue(__instance, out var old) && old != chest) room.roomChests.Remove(old);
            LastChest[__instance] = chest;
            room.roomChests.RemoveAll(c => c == null);
            if (!room.roomChests.Contains(chest)) room.roomChests.Add(chest);
            ChestSync.ResendLater(room, room.roomChests.IndexOf(chest));
        }

        // ---- remote wave ----

        /// <summary>From RoomObjectSync. False = room or shrine not ready yet (retry).</summary>
        public static bool Apply(DungeonRoom room, RoomObject m)
        {
            var at = room.transform.position + new Vector3(m.X, m.Y, 0f);
            EnemyWavesShrine shrine = null;
            float best = 6f;
            foreach (var w in room.GetComponentsInChildren<EnemyWavesShrine>(true))
            {
                float d = Vector2.Distance(w.transform.position, at);
                if (d < best) { best = d; shrine = w; }
            }
            if (shrine == null) return true;
            if (RoomOf(shrine) == null) return false; // not started yet
            string status = Traverse.Create(shrine).Field("currentStatus").GetValue<object>()?.ToString();
            _remote = true;
            try
            {
                if (m.Kind == RoomObject.WaveStart && status != "Battle")
                {
                    Spectating.Add(room);
                    StartWave.Invoke(shrine, null);
                }
                else if (m.Kind == RoomObject.WaveDone && status == "Battle")
                    Finish.Invoke(shrine, null);
            }
            catch (Exception e) { CoopPlugin.Log.LogWarning("Could not follow an enemy wave: " + e.Message); }
            finally { _remote = false; }
            return true;
        }
    }
}
