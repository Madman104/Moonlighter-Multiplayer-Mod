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
    /// Pots, crates and skeletons break for everyone. The game that breaks one rolls its drop
    /// (shared by FloorItems) and sends where it was; the other games break the one at that
    /// spot without a drop of their own. Breakables are found by position in the room (their
    /// layout comes from the shared floor), not by order.
    /// </summary>
    internal static class BreakableSync
    {
        private const float MatchDistance = 6f;

        private static readonly HashSet<Enemy> RemoteBroken = new HashSet<Enemy>();

        public static void Apply(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(BreakableObjectBehaviour), nameof(BreakableObjectBehaviour.BreakableDeath)),
                postfix: new HarmonyMethod(typeof(BreakableSync), nameof(BrokePostfix)));
            harmony.Patch(AccessTools.Method(typeof(BreakableObjectBehaviour), "DropObjects"),
                prefix: new HarmonyMethod(typeof(BreakableSync), nameof(SkipIfRemote)));
            harmony.Patch(AccessTools.Method(typeof(Enemy), nameof(Enemy.DropItemsOnDeath)),
                prefix: new HarmonyMethod(typeof(BreakableSync), nameof(SkipEnemyDropIfRemote)));
        }

        public static void Attach(CoopLink session)
        {
            RemoteBroken.Clear();
            session.GameMessageReceived += (type, msg) =>
            {
                if (type == MessageType.BreakableBroken) OnBroken((BreakableBroken)msg);
            };
            session.StateChanged += st => { if (st == SessionState.Closed) RemoteBroken.Clear(); };
        }

        private static bool Sharing(out CoopLink s)
        {
            s = CoopManager.Instance != null ? CoopManager.Instance.Session : null;
            return s != null && s.State == SessionState.Connected && SharedCultures.InSharedDungeon() && !FloorSync.ClientBuilding;
        }

        private static DungeonRoom RoomOf(BreakableObjectBehaviour b)
        {
            var room = b.enemy != null ? b.enemy.enemyRoom : null;
            return room != null ? room : b.GetComponentInParent<DungeonRoom>();
        }

        private static bool SkipIfRemote(BreakableObjectBehaviour __instance) => !(__instance.enemy != null && RemoteBroken.Contains(__instance.enemy));

        private static bool SkipEnemyDropIfRemote(Enemy __instance) => !RemoteBroken.Contains(__instance);

        private static void BrokePostfix(BreakableObjectBehaviour __instance)
        {
            if (__instance == null || __instance.enemy == null || RemoteBroken.Contains(__instance.enemy) || !Sharing(out var s)) return;
            var room = RoomOf(__instance);
            if (room == null) return;
            var local = __instance.transform.position - room.transform.position;
            s.SendGame(MessageType.BreakableBroken, new BreakableBroken
            {
                RoomKey = EnemyOwnership.RoomKey(room.x, room.y, room.isSecret),
                X = local.x,
                Y = local.y,
            });
        }

        private static void OnBroken(BreakableBroken msg)
        {
            if (!Sharing(out _)) return;
            var room = FloorItems.FindRoom(msg.RoomKey);
            if (room == null) return;
            var at = room.transform.position + new Vector3(msg.X, msg.Y, 0f);
            BreakableObjectBehaviour best = null;
            float bestD = MatchDistance;
            foreach (var b in room.GetComponentsInChildren<BreakableObjectBehaviour>(true))
            {
                if (b == null || b.enemy == null || b.enemy.enemyStats == null || !b.enemy.enemyStats.isAlive) continue;
                float d = Vector2.Distance(b.transform.position, at);
                if (d < bestD) { bestD = d; best = b; }
            }
            if (best == null) return; // already broken here
            RemoteBroken.Add(best.enemy);
            try { EnemySync.SetHealth(best.enemy.enemyStats, 0f); }
            catch (Exception e) { CoopPlugin.Log.LogWarning("Could not break a shared breakable: " + e.Message); }
        }
    }
}
