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
    /// Room objects whose use is shared, found by position in the room:
    /// - Shrines (pressing one opens the room's doors): used in one game = used everywhere.
    /// - Healing pools: what one player drinks is gone for everyone (sent as the fraction left,
    ///   since a pool's size can differ per player). Reviving ghosts already empties a pool.
    /// A late joiner gets both (ResendTo).
    /// </summary>
    internal static class RoomObjectSync
    {
        private const float MatchDistance = 6f;
        private const float PoolInterval = 0.25f;

        private static readonly Dictionary<HealingArea, float> LastPool = new Dictionary<HealingArea, float>();
        private static bool _remote;
        private static float _nextPool;

        public static void Apply(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(Shrine), nameof(Shrine.OnButtonPressed)),
                prefix: new HarmonyMethod(typeof(RoomObjectSync), nameof(ShrinePrefix)),
                postfix: new HarmonyMethod(typeof(RoomObjectSync), nameof(ShrinePostfix)));
        }

        public static void Attach(CoopLink session)
        {
            LastPool.Clear();
            session.GameMessageReceived += (type, msg) =>
            {
                if (type != MessageType.RoomObject) return;
                var m = (RoomObject)msg;
                int scene = GameInfo.CurrentSceneId();
                LateApply.Run("room object " + m.Kind, () => GameInfo.CurrentSceneId() != scene || TryApply(m));
            };
            session.StateChanged += st => { if (st == SessionState.Closed) LastPool.Clear(); };
        }

        private static bool Sharing(out CoopLink s)
        {
            s = CoopManager.Instance != null ? CoopManager.Instance.Session : null;
            return s != null && s.State == SessionState.Connected && SharedCultures.InSharedDungeon() && !FloorSync.ClientBuilding;
        }

        private static RoomObject Describe(DungeonRoom room, Component c, byte kind, float value)
        {
            var local = c.transform.position - room.transform.position;
            return new RoomObject { RoomKey = EnemyOwnership.RoomKey(room.x, room.y, room.isSecret), Kind = kind, X = local.x, Y = local.y, Value = value };
        }

        private static T Nearest<T>(DungeonRoom room, RoomObject m) where T : Component
        {
            var at = room.transform.position + new Vector3(m.X, m.Y, 0f);
            T best = null;
            float bestD = MatchDistance;
            foreach (var c in room.GetComponentsInChildren<T>(true))
            {
                float d = Vector2.Distance(c.transform.position, at);
                if (d < bestD) { bestD = d; best = c; }
            }
            return best;
        }

        // ---- shrines ----

        private static void ShrinePrefix(Shrine __instance, out bool __state) => __state = Traverse.Create(__instance).Field("used").GetValue<bool>();

        private static void ShrinePostfix(Shrine __instance, bool __state)
        {
            if (__state || _remote || !Traverse.Create(__instance).Field("used").GetValue<bool>() || !Sharing(out var s)) return;
            var room = __instance.GetComponentInParent<DungeonRoom>();
            if (room != null) s.SendGame(MessageType.RoomObject, Describe(room, __instance, RoomObject.Shrine, 1f));
        }

        // ---- pools ----

        private static float Fraction(HealingArea pool)
        {
            float full = pool.initialHealingHealth;
            float left = Traverse.Create(pool).Field("healingHealth").GetValue<float>();
            return full > 0f ? Mathf.Clamp01(left / full) : 0f;
        }

        private static void SetFraction(HealingArea pool, float fraction)
        {
            var t = Traverse.Create(pool);
            t.Field("healingHealth").SetValue(fraction * pool.initialHealingHealth);
            float dark = t.Field("darknessTint").GetValue<float>();
            float c = (fraction * (255f - dark) + dark) / 255f;
            if (pool.render != null) pool.render.color = new Color(c, c, c, 1f);
            if (pool.renderExtraLayer != null) pool.renderExtraLayer.color = new Color(c, c, c, 1f);
        }

        /// <summary>Every frame from CoopManager: send what the local player drank from pools here.</summary>
        public static void Tick()
        {
            if (Time.realtimeSinceStartup < _nextPool) return;
            _nextPool = Time.realtimeSinceStartup + PoolInterval;
            if (!Sharing(out var s)) return;
            var room = DungeonManager.Instance.currentDungeonRoom;
            if (room == null) return;
            foreach (var pool in room.GetComponentsInChildren<HealingArea>(false))
            {
                float f = Fraction(pool);
                if (LastPool.TryGetValue(pool, out float last) && f < last - 0.001f)
                    s.SendGame(MessageType.RoomObject, Describe(room, pool, RoomObject.Pool, f));
                LastPool[pool] = f;
            }
        }

        // ---- receiving ----

        private static bool TryApply(RoomObject m)
        {
            if (!Sharing(out _)) return false;
            var room = FloorItems.FindRoom(m.RoomKey);
            if (room == null) return false;
            if (m.Kind == RoomObject.Shrine)
            {
                var shrine = Nearest<Shrine>(room, m);
                if (shrine == null) return true;
                var t = Traverse.Create(shrine);
                if (t.Field("used").GetValue<bool>()) return true;
                if (t.Field("myRoom").GetValue<DungeonRoom>() == null) return false; // not started yet
                _remote = true;
                try { shrine.OnButtonPressed(); }
                finally { _remote = false; }
                return true;
            }
            if (m.Kind == RoomObject.Pool)
            {
                var pool = Nearest<HealingArea>(room, m);
                if (pool == null) return true;
                if (m.Value < Fraction(pool)) SetFraction(pool, m.Value);
                LastPool[pool] = Fraction(pool);
                return true;
            }
            return true;
        }

        /// <summary>Host: a guest joined late; send used shrines and drunk pools on this floor.</summary>
        public static void ResendTo(CoopLink s, int guest)
        {
            var dm = DungeonManager.Instance;
            if (dm == null || !SharedCultures.InSharedDungeon()) return;
            var rooms = new List<DungeonRoom>(dm.dungeonRooms);
            if (dm.bossRoom != null && !rooms.Contains(dm.bossRoom)) rooms.Add(dm.bossRoom);
            foreach (var room in rooms)
            {
                if (room == null) continue;
                foreach (var shrine in room.GetComponentsInChildren<Shrine>(true))
                    if (Traverse.Create(shrine).Field("used").GetValue<bool>())
                        s.SendGameTo(guest, MessageType.RoomObject, Describe(room, shrine, RoomObject.Shrine, 1f));
                foreach (var pool in room.GetComponentsInChildren<HealingArea>(true))
                {
                    float f = Fraction(pool);
                    if (f < 0.999f) s.SendGameTo(guest, MessageType.RoomObject, Describe(room, pool, RoomObject.Pool, f));
                }
            }
        }
    }
}
