using System;
using System.Collections;
using Coop.Core.Dungeon;
using Coop.Core.Protocol;
using Coop.Core.Session;
using Coop.Plugin.Runtime;
using FullSerializer;
using HarmonyLib;
using UnityEngine;

namespace Coop.Plugin.Dungeon
{
    /// <summary>
    /// Gives both players the same dungeon floor. The host generates it normally and sends
    /// the game's own save description of it; the client builds the floor from that
    /// description with the game's "resume saved dungeon" code.
    /// </summary>
    internal static class FloorSync
    {
        public const float ClientWaitSeconds = 30f;

        private static readonly FloorLayoutStore Layouts = new FloorLayoutStore();
        private static bool _bypassPrefix;
        private static bool _hooked;
        private static int _travelToken;

        /// <summary>Host: the next floor is an emblem-portal return (start where the host stands).</summary>
        public static bool HostEmblemTrip;

        /// <summary>True while the client is building (or waiting for) the host's floor.</summary>
        public static bool ClientBuilding { get; private set; }

        /// <summary>Raised on the client when the host's floor is built and playable.</summary>
        public static event Action ClientFloorReady;

        public static void Apply(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(DungeonGenerator), nameof(DungeonGenerator.GenerateDungeon),
                    new[] { typeof(ItemMaster.Culture), typeof(int), typeof(bool) }),
                prefix: new HarmonyMethod(typeof(FloorSync), nameof(GeneratePrefix)));
            // The saved-floor builder reveals the whole minimap at the end (meant for emblem
            // returns). On the guest's copy of a fresh floor, rooms should appear as visited.
            harmony.Patch(AccessTools.Method(typeof(MinimapPanel), "Reveal"),
                prefix: new HarmonyMethod(typeof(FloorSync), nameof(RevealPrefix)));
        }

        private static bool RevealPrefix() => !(ClientBuilding && Connected(out var s) && !s.IsHost);

        /// <summary>Called when a co-op session starts.</summary>
        public static void Attach(PeerSession session)
        {
            NewTravel();
            session.GameMessageReceived += OnGameMessage;
            if (!_hooked)
            {
                _hooked = true;
                DungeonGenerator.OnGeneratedDungeon += OnGenerated;
            }
        }

        /// <summary>
        /// Any dungeon or town travel: older layouts and pending waits no longer apply.
        /// </summary>
        public static void NewTravel()
        {
            _travelToken++;
            Layouts.Clear();
            ClientBuilding = false;
        }

        private static bool Connected(out PeerSession s)
        {
            s = CoopManager.Instance != null ? CoopManager.Instance.Session : null;
            return s != null && s.State == SessionState.Connected;
        }

        /// <summary>Only the four regular dungeons are generated from rooms; others are prefabs.</summary>
        private static bool IsSharedCulture(ItemMaster.Culture c) => SharedCultures.Contains(c);

        private static void OnGameMessage(MessageType type, object msg)
        {
            if (type != MessageType.FloorLayout) return;
            var f = (FloorLayout)msg;
            Layouts.Put(f.Culture, f.Floor, f.Json);
            CoopPlugin.Log.LogInfo($"Received floor layout {(ItemMaster.Culture)f.Culture} {f.Floor} ({f.Json.Length} chars)");
        }

        private static void OnGenerated()
        {
            if (!Connected(out var s)) return;
            if (!s.IsHost)
            {
                if (ClientBuilding)
                {
                    ClientBuilding = false;
                    ClientFloorReady?.Invoke();
                }
                return;
            }

            var dm = DungeonManager.Instance;
            if (dm == null || !IsSharedCulture(dm.culture)) return;
            // The saved-floor builder starts in the "portal room", or in the boss room
            // (killing its enemies) when there is none. Point it at the first room.
            // After an emblem-portal return, start in the room the host is in (the portal room).
            var startRoom = HostEmblemTrip && dm.currentDungeonRoom != null ? dm.currentDungeonRoom : dm.initialDungeonRoom;
            HostEmblemTrip = false;
            SendLayout(s, dm, startRoom);
        }

        /// <summary>
        /// Host: a guest finished loading after the host had already gone into a dungeon. Send
        /// the floor as it is now, starting in the host's room (cleared rooms stay cleared).
        /// </summary>
        public static void SendCurrentFloor(PeerSession s)
        {
            var dm = DungeonManager.Instance;
            if (dm == null || !IsSharedCulture(dm.culture) || dm.currentDungeonRoom == null) return;
            SendLayout(s, dm, dm.currentDungeonRoom);
        }

        private static void SendLayout(PeerSession s, DungeonManager dm, DungeonRoom startRoom)
        {
            try
            {
                var info = new DungeonSaveInfo(dm);
                int start = info.rooms.FindIndex(r => startRoom != null
                    && r.x == startRoom.x && r.y == startRoom.y && r.isSecret == startRoom.isSecret);
                if (start >= 0)
                {
                    info.portalRoomIndex = start;
                    info.portalPosition = startRoom == dm.currentDungeonRoom && HeroMerchant.Instance != null
                        ? HeroMerchant.Instance.transform.position
                        : startRoom.transform.position;
                }
                FsJSONManager.Serialize(info, out fsData data);
                var json = data.ToString();
                s.SendGame(MessageType.FloorLayout, new FloorLayout { Culture = (byte)info.culture, Floor = (byte)info.floor, Json = json });
                CoopPlugin.Log.LogInfo($"Sent floor layout {info.culture} {info.floor} ({json.Length} chars, {info.rooms.Count} rooms)");
            }
            catch (Exception e)
            {
                CoopPlugin.Log.LogError("Could not send floor layout: " + e);
            }
        }

        // Client: build the host's floor instead of a random one.
        private static bool GeneratePrefix(ItemMaster.Culture culture, int level, bool test, ref IEnumerator __result)
        {
            if (_bypassPrefix || test || !IsSharedCulture(culture) || !Connected(out var s) || s.IsHost) return true;
            __result = ClientFloor(culture, level, _travelToken);
            return false;
        }

        private static IEnumerator ClientFloor(ItemMaster.Culture culture, int level, int token)
        {
            ClientBuilding = true;
            CoopPlugin.Log.LogInfo($"Waiting for the host's {culture} floor {level}...");
            float deadline = Time.realtimeSinceStartup + ClientWaitSeconds;
            string json = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (token != _travelToken) { ClientBuilding = false; yield break; } // a newer trip replaced this one
                if (Layouts.TryTake((int)culture, level, out json)) break;
                if (!Connected(out _)) break;
                yield return null;
            }
            if (token != _travelToken) { ClientBuilding = false; yield break; }
            if (GameManager.Instance != null && !GameManager.Instance.IsDungeonSceneLoaded()) { ClientBuilding = false; yield break; }

            DungeonSaveInfo info = null;
            if (json != null)
            {
                try
                {
                    FsJSONManager.Deserialize(json, ref info, false);
                }
                catch (Exception e)
                {
                    CoopPlugin.Log.LogError("Could not read the host's floor layout: " + e);
                    info = null;
                }
            }

            if (info != null)
            {
                CoopPlugin.Log.LogInfo($"Building the host's {culture} floor {level} ({info.rooms.Count} rooms)");
                yield return DungeonGenerator.GenerateSavedDungeon(info);
                yield break;
            }

            ClientBuilding = false;
            CoopPlugin.Log.LogWarning($"No floor layout from the host for {culture} floor {level}; generating locally");
            // A different floor than the host's can't be shared: end the session rather than
            // let the two games drift apart room by room.
            if (Connected(out _))
            {
                CoopToast.Show("Couldn't get the host's floor - left co-op");
                CoopManager.Instance?.Leave();
            }
            IEnumerator own;
            _bypassPrefix = true;
            try { own = DungeonGenerator.GenerateDungeon(culture, level, false); }
            finally { _bypassPrefix = false; }
            yield return own;
        }
    }
}
