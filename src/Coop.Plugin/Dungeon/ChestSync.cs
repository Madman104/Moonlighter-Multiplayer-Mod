using System;
using System.Collections.Generic;
using Coop.Core.Dungeon;
using Coop.Core.Protocol;
using Coop.Core.Session;
using Coop.Plugin.Runtime;
using FullSerializer;
using HarmonyLib;
using UnityEngine;

namespace Coop.Plugin.Dungeon
{
    /// <summary>A dungeon chest's content, as sent between the games.</summary>
    [Serializable]
    public class ChestSnapshot
    {
        public List<ItemSaveInfo> items = new List<ItemSaveInfo>();
    }

    /// <summary>
    /// Dungeon chests are shared: one content for everyone.
    /// - Room chests (type, mimic or not, first content) are rolled with Random seeded per room,
    ///   so every game spawns the same chests; mimics get enemy ids so they are shared enemies.
    /// - The host sends each chest's content once per floor; the others replace theirs (covers
    ///   anything rolled differently, e.g. amulets one player already owns).
    /// - One player at a time: opening a chest locks it for the others until it is closed.
    ///   On closing, the content is sent and everyone else takes it.
    /// Chests are identified by room and their place in room.roomChests (spawn order).
    /// </summary>
    internal static class ChestSync
    {
        public const int MimicIdBase = 200;

        private sealed class SpawnState { public bool Seeded; public UnityEngine.Random.State Saved; public int EnemiesBefore; }

        private static readonly Dictionary<long, int> LockedBy = new Dictionary<long, int>();
        private static readonly HashSet<long> SentInitial = new HashSet<long>();
        private static readonly Dictionary<long, string> Pending = new Dictionary<long, string>();
        private static int _scene = int.MinValue;

        public static void Apply(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(DungeonRoom), nameof(DungeonRoom.SpawnChests)),
                prefix: new HarmonyMethod(typeof(ChestSync), nameof(SpawnPrefix)),
                finalizer: new HarmonyMethod(typeof(ChestSync), nameof(SpawnFinalizer)));
            harmony.Patch(AccessTools.Method(typeof(Chest), nameof(Chest.OpenChest)),
                prefix: new HarmonyMethod(typeof(ChestSync), nameof(OpenPrefix)),
                postfix: new HarmonyMethod(typeof(ChestSync), nameof(OpenPostfix)));
            harmony.Patch(AccessTools.Method(typeof(Chest), nameof(Chest.CloseChest)),
                prefix: new HarmonyMethod(typeof(ChestSync), nameof(ClosePrefix)),
                postfix: new HarmonyMethod(typeof(ChestSync), nameof(ClosePostfix)));
        }

        public static void Attach(CoopLink session)
        {
            Clear();
            session.GameMessageReceived += (type, msg) =>
            {
                if (type == MessageType.ChestUse) OnUse(session.LastFrom, (ChestUse)msg);
                else if (type == MessageType.ChestContents) OnContents((ChestContents)msg);
            };
            session.PeerLeft += i =>
            {
                var free = new List<long>();
                foreach (var kv in LockedBy) if (kv.Value == i) free.Add(kv.Key);
                foreach (var k in free) LockedBy.Remove(k);
            };
            session.StateChanged += st => { if (st == SessionState.Closed) Clear(); };
        }

        private static void Clear()
        {
            LockedBy.Clear();
            SentInitial.Clear();
            Pending.Clear();
        }

        private static bool Sharing(out CoopLink s)
        {
            s = CoopManager.Instance != null ? CoopManager.Instance.Session : null;
            return s != null && s.State == SessionState.Connected && SharedCultures.InSharedDungeon();
        }

        private static long Id(int roomKey, int index) => ((long)roomKey << 8) | (uint)(index & 0xFF);

        private static bool IdOf(Chest chest, out DungeonRoom room, out int index, out long id)
        {
            room = chest != null ? chest.GetComponentInParent<DungeonRoom>() : null;
            index = room != null && room.roomChests != null ? room.roomChests.IndexOf(chest) : -1;
            id = index >= 0 && index < 256 ? Id(EnemyOwnership.RoomKey(room.x, room.y, room.isSecret), index) : -1;
            return id >= 0;
        }

        // ---- same chests on every game ----

        private static void SpawnPrefix(DungeonRoom __instance, out SpawnState __state)
        {
            __state = new SpawnState { EnemiesBefore = __instance.roomEnemies != null ? __instance.roomEnemies.Count : 0 };
            if (!Sharing(out _)) return;
            var dm = DungeonManager.Instance;
            __state.Saved = UnityEngine.Random.state;
            __state.Seeded = true;
            UnityEngine.Random.InitState(RoomSeed.For((int)dm.culture, dm.level, __instance.x, __instance.y, __instance.isSecret) ^ 0x5EED);
        }

        private static Exception SpawnFinalizer(Exception __exception, DungeonRoom __instance, SpawnState __state)
        {
            if (__state == null) return __exception;
            if (__state.Seeded) UnityEngine.Random.state = __state.Saved;
            try
            {
                var list = __instance.roomEnemies;
                int n = 0;
                for (int i = __state.EnemiesBefore; list != null && i < list.Count; i++)
                {
                    var e = list[i];
                    if (e == null || e.GetComponent<CoopEnemyId>() != null) continue;
                    e.gameObject.AddComponent<CoopEnemyId>().Id = MimicIdBase + n++;
                }
            }
            catch (Exception e) { CoopPlugin.Log.LogWarning("Could not tag mimics: " + e.Message); }
            return __exception;
        }

        // ---- host: initial content ----

        /// <summary>Every frame from CoopManager.</summary>
        public static void Tick()
        {
            if (!Sharing(out var s)) return;
            int scene = GameInfo.CurrentSceneId();
            if (scene != _scene) { _scene = scene; Clear(); return; }
            if (!s.IsHost || FloorSync.ClientBuilding) return;
            var room = DungeonManager.Instance.currentDungeonRoom;
            if (room == null || room.roomChests == null) return;
            for (int i = 0; i < room.roomChests.Count && i < 256; i++)
            {
                var chest = room.roomChests[i];
                long id = Id(EnemyOwnership.RoomKey(room.x, room.y, room.isSecret), i);
                if (chest == null || SentInitial.Contains(id) || chest.isChestOpened) continue;
                SentInitial.Add(id);
                Send(s, room, i, chest);
            }
        }

        private static void Send(CoopLink s, DungeonRoom room, int index, Chest chest)
        {
            try
            {
                var snap = new ChestSnapshot();
                foreach (var stack in chest.ChestContent) snap.items.Add(stack != null ? new ItemSaveInfo(stack) : new ItemSaveInfo());
                FsJSONManager.Serialize(snap, out fsData data);
                s.SendGame(MessageType.ChestContents, new ChestContents
                {
                    RoomKey = EnemyOwnership.RoomKey(room.x, room.y, room.isSecret),
                    Index = (byte)index,
                    Json = data.ToString(),
                });
            }
            catch (Exception e) { CoopPlugin.Log.LogWarning("Could not send a chest: " + e.Message); }
        }

        // ---- one player at a time ----

        private static bool OpenPrefix(Chest __instance, out bool __state)
        {
            __state = __instance != null && __instance.isChestOpened;
            if (!Sharing(out _) || !IdOf(__instance, out _, out _, out long id)) return true;
            if (!LockedBy.TryGetValue(id, out int who)) return true;
            CoopToast.Show(PlayerNames.Name(who) + " is using this chest");
            return false;
        }

        private static void OpenPostfix(Chest __instance, bool __state)
        {
            if (__state || __instance == null || !__instance.isChestOpened) return;
            if (!Sharing(out var s) || !IdOf(__instance, out var room, out int index, out _)) return;
            s.SendGame(MessageType.ChestUse, new ChestUse
            {
                RoomKey = EnemyOwnership.RoomKey(room.x, room.y, room.isSecret), Index = (byte)index, Open = true,
            });
        }

        private static void ClosePrefix(Chest __instance, out bool __state) => __state = __instance != null && __instance.isChestOpened;

        private static void ClosePostfix(Chest __instance, bool __state)
        {
            if (!__state || !Sharing(out var s) || !IdOf(__instance, out var room, out int index, out long id)) return;
            Send(s, room, index, __instance);
            s.SendGame(MessageType.ChestUse, new ChestUse
            {
                RoomKey = EnemyOwnership.RoomKey(room.x, room.y, room.isSecret), Index = (byte)index, Open = false,
            });
            Pending.Remove(id); // someone closed it while we had it open too: our later close wins everywhere
        }

        // ---- receiving ----

        private static void OnUse(int from, ChestUse msg)
        {
            long id = Id(msg.RoomKey, msg.Index);
            if (msg.Open) LockedBy[id] = from;
            else LockedBy.Remove(id);
        }

        private static void OnContents(ChestContents msg)
        {
            if (!Sharing(out _)) return;
            var room = FloorItems.FindRoom(msg.RoomKey);
            if (room == null || room.roomChests == null || msg.Index >= room.roomChests.Count) return;
            var chest = room.roomChests[msg.Index];
            if (chest == null) return;
            if (chest.isChestOpened) { Pending[Id(msg.RoomKey, msg.Index)] = msg.Json; return; } // both opened at once: ours is sent on close
            ApplyJson(chest, msg.Json);
        }

        private static void ApplyJson(Chest chest, string json)
        {
            try
            {
                ChestSnapshot snap = null;
                FsJSONManager.Deserialize(json, ref snap, false);
                if (snap == null || snap.items == null) return;
                var content = chest.ChestContent;
                for (int i = 0; i < content.Count; i++)
                {
                    var want = i < snap.items.Count ? snap.items[i] : null;
                    var have = content[i];
                    if (Shared.ItemFactory.Same(have, want)) continue;
                    chest.SetItem(null, i);
                    Shared.ItemFactory.Destroy(have);
                    var stack = Shared.ItemFactory.Build(want);
                    if (stack != null) chest.SetItem(stack, i);
                }
                bool empty = chest.ChestContent.TrueForAll(x => x == null);
                var animator = Traverse.Create(chest).Field("_chestAnimator").GetValue<Animator>();
                if (animator != null && empty) animator.SetBool("CloseChestEmpty", true);
            }
            catch (Exception e) { CoopPlugin.Log.LogWarning("Could not apply a shared chest: " + e.Message); }
        }
    }
}
