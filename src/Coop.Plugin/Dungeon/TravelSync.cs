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
    /// Same-room rule: when one player enters a dungeon, changes floor, changes room or
    /// returns to town, the other player's game does the same. Conflicts: host wins.
    /// </summary>
    internal static class TravelSync
    {
        /// <summary>True while replaying the other player's travel (so it isn't sent back).</summary>
        private static bool _applyingRemote;
        private static TravelArbiter _arbiter;

        // Floor this game is already loading on its own, so a duplicate request is ignored.
        private static int _pendingCulture = -1, _pendingLevel = -1;
        private static float _pendingUntil;

        public static void Apply(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(GameManager), nameof(GameManager.LoadDungeonScene)),
                prefix: new HarmonyMethod(typeof(TravelSync), nameof(LoadDungeonPrefix)));
            harmony.Patch(AccessTools.Method(typeof(GameManager), nameof(GameManager.LoadTownSceneFromDungeon)),
                prefix: new HarmonyMethod(typeof(TravelSync), nameof(LoadTownPrefix)));
            harmony.Patch(AccessTools.Method(typeof(DungeonManager), nameof(DungeonManager.ChangeRoomTo)),
                prefix: new HarmonyMethod(typeof(TravelSync), nameof(ChangeRoomPrefix)));
            harmony.Patch(AccessTools.Method(typeof(DungeonManager), nameof(DungeonManager.SpecialChangeRoomTo)),
                prefix: new HarmonyMethod(typeof(TravelSync), nameof(SpecialChangeRoomPrefix)));
        }

        public static void Attach(PeerSession session)
        {
            _arbiter = new TravelArbiter(session.IsHost);
            _pendingCulture = _pendingLevel = -1;
            session.GameMessageReceived += OnGameMessage;
        }

        private static float Now => Time.realtimeSinceStartup;

        private static bool Connected(out PeerSession s)
        {
            s = CoopManager.Instance != null ? CoopManager.Instance.Session : null;
            return s != null && s.State == SessionState.Connected && _arbiter != null;
        }

        private static void SendLocal(MessageType type, object msg)
        {
            if (_applyingRemote || !Connected(out var s)) return;
            _arbiter.LocalTravel(Now);
            s.SendGame(type, msg);
        }

        // ---- local travel → tell the other player ----

        private static void LoadDungeonPrefix(ItemMaster.Culture culture, int level, bool replay, bool isTest, bool fromEmblem)
        {
            if (isTest) return;
            _pendingCulture = (int)culture;
            _pendingLevel = level;
            _pendingUntil = Now + 15f;
            // Emblem returns rebuild a floor from this player's own emblem save; not shared yet.
            if (fromEmblem) return;
            SendLocal(MessageType.DungeonTravel, new DungeonTravel { Culture = (byte)culture, Level = (byte)level, Replay = replay });
        }

        private static void LoadTownPrefix(bool fromEmblem)
        {
            _pendingCulture = _pendingLevel = -1;
            SendLocal(MessageType.TownTravel, new TownTravel { FromEmblem = fromEmblem });
        }

        private static void ChangeRoomPrefix(DungeonManager __instance, DungeonRoom to, DungeonDoor door)
        {
            if (to == null) return;
            var from = FindDoorRoom(__instance, door, out int doorIndex);
            SendLocal(MessageType.RoomChange, new RoomChange
            {
                FromX = (short)(from != null ? from.x : 0),
                FromY = (short)(from != null ? from.y : 0),
                ToX = (short)to.x,
                ToY = (short)to.y,
                DoorIndex = (sbyte)doorIndex,
            });
        }

        private static void SpecialChangeRoomPrefix(DungeonManager __instance, DungeonRoom to, bool teleportToLastDoor)
        {
            if (to == null) return;
            var cur = __instance.currentDungeonRoom;
            SendLocal(MessageType.RoomChange, new RoomChange
            {
                FromX = (short)(cur != null ? cur.x : 0),
                FromY = (short)(cur != null ? cur.y : 0),
                ToX = (short)to.x,
                ToY = (short)to.y,
                DoorIndex = -1,
                Special = true,
                TeleportToLastDoor = teleportToLastDoor,
            });
        }

        private static DungeonRoom FindDoorRoom(DungeonManager dm, DungeonDoor door, out int index)
        {
            index = -1;
            if (door == null) return dm.currentDungeonRoom;
            var cur = dm.currentDungeonRoom;
            if (cur != null && (index = cur.roomDoors.IndexOf(door)) >= 0) return cur;
            foreach (var room in dm.dungeonRooms)
                if (room != null && (index = room.roomDoors.IndexOf(door)) >= 0) return room;
            return cur;
        }

        // ---- the other player's travel → do the same here ----

        private static void OnGameMessage(MessageType type, object msg)
        {
            if (type != MessageType.DungeonTravel && type != MessageType.TownTravel && type != MessageType.RoomChange) return;
            if (!_arbiter.ShouldApplyRemote(Now))
            {
                CoopPlugin.Log.LogInfo($"Ignoring the other player's {type}: host travelled at the same time");
                return;
            }
            _applyingRemote = true;
            try
            {
                switch (type)
                {
                    case MessageType.DungeonTravel: FollowDungeon((DungeonTravel)msg); break;
                    case MessageType.TownTravel: FollowTown((TownTravel)msg); break;
                    case MessageType.RoomChange: FollowRoom((RoomChange)msg); break;
                }
            }
            catch (Exception e)
            {
                CoopPlugin.Log.LogError($"Could not follow the other player ({type}): {e}");
            }
            finally
            {
                _applyingRemote = false;
            }
        }

        private static void FollowDungeon(DungeonTravel t)
        {
            var gm = GameManager.Instance;
            if (gm == null) return;
            bool alreadyGoing = _pendingCulture == t.Culture && _pendingLevel == t.Level && Now < _pendingUntil;
            var dm = DungeonManager.Instance;
            bool alreadyThere = gm.IsDungeonSceneLoaded() && dm != null && (int)dm.culture == t.Culture && dm.level == t.Level;
            if (alreadyGoing || alreadyThere) return;
            CoopPlugin.Log.LogInfo($"Following the other player to {(ItemMaster.Culture)t.Culture} floor {t.Level}");
            gm.LoadDungeonScene((ItemMaster.Culture)t.Culture, t.Level, t.Replay);
        }

        private static void FollowTown(TownTravel t)
        {
            var gm = GameManager.Instance;
            if (gm == null || !gm.IsDungeonSceneLoaded()) return; // already in town or on the way
            CoopPlugin.Log.LogInfo("Following the other player back to town");
            gm.LoadTownSceneFromDungeon(t.FromEmblem);
        }

        private static void FollowRoom(RoomChange r)
        {
            var dm = DungeonManager.Instance;
            if (dm == null) return;
            var to = FindRoom(dm, r.ToX, r.ToY);
            if (to == null)
            {
                CoopPlugin.Log.LogWarning($"Room ({r.ToX},{r.ToY}) not found; floors differ?");
                return;
            }
            if (dm.currentDungeonRoom == to) return;

            if (r.Special || r.DoorIndex < 0)
            {
                dm.SpecialChangeRoomTo(to, r.TeleportToLastDoor);
                return;
            }
            var from = FindRoom(dm, r.FromX, r.FromY);
            var door = from != null && r.DoorIndex < from.roomDoors.Count ? from.roomDoors[r.DoorIndex] : null;
            if (door == null)
            {
                dm.SpecialChangeRoomTo(to, false);
                return;
            }
            dm.ChangeRoomTo(to, door);
        }

        private static DungeonRoom FindRoom(DungeonManager dm, int x, int y)
        {
            foreach (var room in dm.dungeonRooms)
                if (room != null && room.x == x && room.y == y) return room;
            if (dm.bossRoom != null && dm.bossRoom.x == x && dm.bossRoom.y == y) return dm.bossRoom;
            return null;
        }
    }
}
