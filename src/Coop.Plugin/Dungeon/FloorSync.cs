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

        public static void Apply(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(DungeonGenerator), nameof(DungeonGenerator.GenerateDungeon),
                    new[] { typeof(ItemMaster.Culture), typeof(int), typeof(bool) }),
                prefix: new HarmonyMethod(typeof(FloorSync), nameof(GeneratePrefix)));
        }

        /// <summary>Called when a co-op session starts.</summary>
        public static void Attach(PeerSession session)
        {
            Layouts.Clear();
            session.GameMessageReceived += OnGameMessage;
            if (!_hooked)
            {
                _hooked = true;
                DungeonGenerator.OnGeneratedDungeon += OnGenerated;
            }
        }

        private static bool Connected(out PeerSession s)
        {
            s = CoopManager.Instance != null ? CoopManager.Instance.Session : null;
            return s != null && s.State == SessionState.Connected;
        }

        private static void OnGameMessage(MessageType type, object msg)
        {
            if (type != MessageType.FloorLayout) return;
            var f = (FloorLayout)msg;
            Layouts.Put(f.Culture, f.Floor, f.Json);
            CoopPlugin.Log.LogInfo($"Received floor layout {(ItemMaster.Culture)f.Culture} {f.Floor} ({f.Json.Length} chars)");
        }

        // Host: a floor finished generating, send its layout.
        private static void OnGenerated()
        {
            if (!Connected(out var s) || !s.IsHost) return;
            var dm = DungeonManager.Instance;
            if (dm == null) return;
            try
            {
                var info = new DungeonSaveInfo(dm);
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
            if (_bypassPrefix || test || !Connected(out var s) || s.IsHost) return true;
            __result = ClientFloor(culture, level);
            return false;
        }

        private static IEnumerator ClientFloor(ItemMaster.Culture culture, int level)
        {
            CoopPlugin.Log.LogInfo($"Waiting for the host's {culture} floor {level}...");
            float deadline = Time.realtimeSinceStartup + ClientWaitSeconds;
            string json = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (Layouts.TryTake((int)culture, level, out json)) break;
                if (!Connected(out _)) break;
                yield return null;
            }

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

            CoopPlugin.Log.LogWarning($"No floor layout from the host for {culture} floor {level}; generating locally");
            IEnumerator own;
            _bypassPrefix = true;
            try { own = DungeonGenerator.GenerateDungeon(culture, level, false); }
            finally { _bypassPrefix = false; }
            yield return own;
        }
    }
}
