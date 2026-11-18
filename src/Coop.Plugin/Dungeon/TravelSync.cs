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
    /// returns to town, the other player's game does the same. Room clashes: host wins.
    /// </summary>
    internal static class TravelSync
    {
        /// <summary>True while replaying the other player's travel (so it isn't sent back).</summary>
        private static bool _applyingRemote;
        private static TravelArbiter _roomArbiter;
        private static RoomChange _queuedRoom;

        // Floor this game is already loading on its own, so a duplicate request is ignored.
        private static int _pendingCulture = -1, _pendingLevel = -1;
        private static float _pendingUntil;
        private static bool _hookedReady;

        public static void Apply(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(GameManager), nameof(GameManager.LoadDungeonScene)),
                prefix: new HarmonyMethod(typeof(TravelSync), nameof(LoadDungeonPrefix)));
            harmony.Patch(AccessTools.Method(typeof(GameManager), nameof(GameManager.LoadTownSceneFromDungeon)),
                prefix: new HarmonyMethod(typeof(TravelSync), nameof(LoadTownPrefix)));
            // The emblem portal skips LoadDungeonScene and starts the loader itself.
            harmony.Patch(AccessTools.Method(typeof(GameManager), nameof(GameManager.LoadDungeonSceneFromEmblemPortal)),
                prefix: new HarmonyMethod(typeof(TravelSync), nameof(EmblemPrefix)));
            harmony.Patch(AccessTools.Method(typeof(DungeonManager), nameof(DungeonManager.ChangeRoomTo)),
                prefix: new HarmonyMethod(typeof(TravelSync), nameof(ChangeRoomPrefix)));
            harmony.Patch(AccessTools.Method(typeof(DungeonManager), nameof(DungeonManager.SpecialChangeRoomTo)),
                prefix: new HarmonyMethod(typeof(TravelSync), nameof(SpecialChangeRoomPrefix)));
        }

        public static void Attach(PeerSession session)
        {
            _roomArbiter = new TravelArbiter(session.IsHost);
            _pendingCulture = _pendingLevel = -1;
            _queuedRoom = null;
            session.GameMessageReceived += OnGameMessage;
            if (!_hookedReady)
            {
                _hookedReady = true;
                FloorSync.ClientFloorReady += ApplyQueuedRoom;
            }
        }

        private static float Now => Time.realtimeSinceStartup;

        private static bool Connected(out PeerSession s)
        {
            s = CoopManager.Instance != null ? CoopManager.Instance.Session : null;
            return s != null && s.State == SessionState.Connected && _roomArbiter != null;
        }

        private static void SendLocal(MessageType type, object msg)
        {
            if (_applyingRemote || !Connected(out var s)) return;
            if (type == MessageType.RoomChange) _roomArbiter.LocalTravel(Now);
            s.SendGame(type, msg);
        }

        // ---- local travel → tell the other player ----

        private static void LoadDungeonPrefix(ItemMaster.Culture culture, int level, bool replay, bool isTest, bool fromEmblem)
        {
            if (isTest) return;
            FloorSync.NewTravel();
            _queuedRoom = null;
            _pendingCulture = (int)culture;
            _pendingLevel = level;
            _pendingUntil = Now + 15f;
            // Emblem returns rebuild a floor from this player's own emblem save; not shared yet.
            if (fromEmblem) return;
            SendLocal(MessageType.DungeonTravel, new DungeonTravel { Culture = (byte)culture, Level = (byte)level, Replay = replay });
        }

        /// <summary>
        /// Emblem-portal return: the host's portal floor is the shared one. The guest's portal
        /// asks the host to go through theirs; the host's trip is sent like any dungeon trip
        /// and the guest builds the host's floor.
        /// </summary>
        private static bool EmblemPrefix(GameManager __instance)
        {
            if (_applyingRemote || !Connected(out var s)) return true;
            if (!s.IsHost)
            {
                s.SendGame(MessageType.EmblemReturn, null);
                CoopToast.Show("Asking the host to open their portal");
                return false;
            }
            var emblem = __instance.currentGameSlot != null ? __instance.currentGameSlot.emblemDungeon : null;
            if (emblem == null) return true;
            FloorSync.NewTravel();
            FloorSync.HostEmblemTrip = true;
            _queuedRoom = null;
            _pendingCulture = (int)emblem.culture;
            _pendingLevel = emblem.floor;
            _pendingUntil = Now + 15f;
            SendLocal(MessageType.DungeonTravel, new DungeonTravel { Culture = (byte)emblem.culture, Level = (byte)emblem.floor, Replay = false });
            return true;
        }

        private static bool HasEmblemPortal(GameManager gm)
        {
            var e = gm.currentGameSlot != null ? gm.currentGameSlot.emblemDungeon : null;
            return e != null && e.rooms != null && e.rooms.Count > 0;
        }

        // Host: the guest stepped into their portal.
        private static void FollowEmblemRequest()
        {
            var gm = GameManager.Instance;
            if (gm == null || !gm.IsTownSceneLoaded()) return;
            if (!HasEmblemPortal(gm))
            {
                CoopToast.Show("Your partner tried a portal, but only the host's portal is shared");
                return;
            }
            CoopPlugin.Log.LogInfo("Partner asked to return through the emblem portal");
            CoopToast.Show("Your partner opened the portal: returning to the dungeon");
            gm.LoadDungeonSceneFromEmblemPortal();
        }

        private static void LoadTownPrefix(bool fromEmblem)
        {
            FloorSync.NewTravel();
            _queuedRoom = null;
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
                FromSecret = from != null && from.isSecret,
                ToX = (short)to.x,
                ToY = (short)to.y,
                ToSecret = to.isSecret,
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
                FromSecret = cur != null && cur.isSecret,
                ToX = (short)to.x,
                ToY = (short)to.y,
                ToSecret = to.isSecret,
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
            if (type == MessageType.EmblemReturn)
            {
                if (Connected(out var hs) && hs.IsHost) FollowEmblemRequest();
                return;
            }
            if (type != MessageType.DungeonTravel && type != MessageType.TownTravel && type != MessageType.RoomChange) return;
            if (type == MessageType.RoomChange)
            {
                // Floor still being built here: remember only the latest room, apply when ready.
                if (FloorSync.ClientBuilding) { _queuedRoom = (RoomChange)msg; return; }
                if (!_roomArbiter.ShouldApplyRemote(Now))
                {
                    CoopPlugin.Log.LogInfo("Ignoring the other player's room change: host moved at the same time");
                    return;
                }
            }
            Replay(() =>
            {
                switch (type)
                {
                    case MessageType.DungeonTravel: FollowDungeon((DungeonTravel)msg); break;
                    case MessageType.TownTravel: FollowTown((TownTravel)msg); break;
                    case MessageType.RoomChange: FollowRoom((RoomChange)msg); break;
                }
            }, type.ToString());
        }

        private static void ApplyQueuedRoom()
        {
            var r = _queuedRoom;
            _queuedRoom = null;
            if (r != null) Replay(() => FollowRoom(r), "queued RoomChange");
        }

        private static void Replay(Action action, string what)
        {
            _applyingRemote = true;
            try { action(); }
            catch (Exception e) { CoopPlugin.Log.LogError($"Could not follow the other player ({what}): {e}"); }
            finally { _applyingRemote = false; }
        }

        private static void FollowDungeon(DungeonTravel t)
        {
            var gm = GameManager.Instance;
            var hm = HeroMerchant.Instance;
            if (gm == null || hm == null) return;
            bool alreadyGoing = _pendingCulture == t.Culture && _pendingLevel == t.Level && Now < _pendingUntil;
            var dm = DungeonManager.Instance;
            bool alreadyThere = gm.IsDungeonSceneLoaded() && dm != null && (int)dm.culture == t.Culture && dm.level == t.Level;
            if (alreadyGoing || alreadyThere) return;

            var culture = (ItemMaster.Culture)t.Culture;
            CoopPlugin.Log.LogInfo($"Following the other player to {culture} floor {t.Level}");
            // Keep Will's own floor counter in step, as HeroMerchant.LoadNextLevelDungeonScene would,
            // so this player's next-floor door, pendant cost and trail use the right floor.
            hm.currentDungeonLevel = t.Level;
            Traverse.Create(hm).Field("_lastDungeonCulture").SetValue(culture);
            hm.heroMerchantController.UpdateTrailByCulture(culture);
            gm.LoadDungeonScene(culture, t.Level, t.Replay);
            var floorChange = Traverse.Create(typeof(HeroMerchant)).Field("OnFloorChange").GetValue() as Delegate;
            floorChange?.DynamicInvoke(culture, t.Level);
        }

        private static void FollowTown(TownTravel t)
        {
            var gm = GameManager.Instance;
            if (gm == null || !gm.IsDungeonSceneLoaded()) return; // already in town or on the way
            CoopPlugin.Log.LogInfo("Following the other player back to town");
            // This player has no emblem save of their own, so always take the normal way home.
            gm.LoadTownSceneFromDungeon(false);
        }

        private static void FollowRoom(RoomChange r)
        {
            var dm = DungeonManager.Instance;
            var hm = HeroMerchant.Instance;
            if (dm == null || hm == null) return;
            var to = FindRoom(dm, r.ToX, r.ToY, r.ToSecret);
            if (to == null)
            {
                CoopPlugin.Log.LogWarning($"Room ({r.ToX},{r.ToY}{(r.ToSecret ? ",secret" : "")}) not found; floors differ?");
                return;
            }
            if (dm.currentDungeonRoom == to) return;

            DungeonDoor door = null;
            if (!r.Special && r.DoorIndex >= 0)
            {
                var from = FindRoom(dm, r.FromX, r.FromY, r.FromSecret);
                if (from != null && r.DoorIndex < from.roomDoors.Count) door = from.roomDoors[r.DoorIndex];
            }

            if (door != null)
            {
                dm.ChangeRoomTo(to, door);
                // In the game the door moves Will, not ChangeRoomTo; do the same here.
                hm.heroMerchantController.TeleportTo(door.playerEnterDestinyRoomPoint.position);
            }
            else
            {
                dm.SpecialChangeRoomTo(to, r.TeleportToLastDoor);
                if (!r.TeleportToLastDoor) hm.heroMerchantController.TeleportTo(to.transform.position);
            }
            TeleportFamiliars();
        }

        private static void TeleportFamiliars()
        {
            try { FamiliarsManager.TeleportFamiliarsToWill(); }
            catch (Exception e) { CoopPlugin.Log.LogWarning("Could not move familiars: " + e.Message); }
        }

        private static DungeonRoom FindRoom(DungeonManager dm, int x, int y, bool secret)
        {
            foreach (var room in dm.dungeonRooms)
                if (room != null && room.x == x && room.y == y && room.isSecret == secret) return room;
            if (dm.bossRoom != null && dm.bossRoom.x == x && dm.bossRoom.y == y) return dm.bossRoom;
            return null;
        }
    }
}
