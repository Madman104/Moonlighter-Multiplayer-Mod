using System;
using Coop.Core.Protocol;
using Coop.Core.Session;
using Coop.Plugin.Runtime;
using FullSerializer;
using UnityEngine;

namespace Coop.Plugin.Shared
{
    /// <summary>What is sent: the town board (building upgrades, unlocked shops) and the banker.</summary>
    [Serializable]
    public class TownSnapshot
    {
        public TownboardSaveInfo townboard;
        public BankerSaveInfo banker;
    }

    /// <summary>
    /// Town upgrades show up for everyone during the session, not only on the next join. The
    /// game reads them live from the current slot (shop level, bed, cash register, which
    /// townspeople are open), so each game sends its town board and banker when they change and
    /// the others put them in their slot (last change wins). Newly opened shops in town appear
    /// at once (VillageManager.UpdateCharacters); a bigger shop building is drawn the next time
    /// that player enters town.
    /// </summary>
    internal static class TownSync
    {
        private const float Interval = 1f;

        private static string _lastSent, _lastApplied;
        private static float _next;

        public static void Attach(CoopLink session)
        {
            _lastSent = _lastApplied = null;
            session.GameMessageReceived += (type, msg) =>
            {
                if (type == MessageType.TownState) Apply(PackedText.Unpack(((TownState)msg).Data));
            };
        }

        private static bool Ready(out CoopLink s, out GameSlot slot)
        {
            s = CoopManager.Instance != null ? CoopManager.Instance.Session : null;
            slot = GameManager.Instance != null ? GameManager.Instance.currentGameSlot : null;
            if (s == null || s.State != SessionState.Connected || slot == null) return false;
            return s.IsHost || WorldSync.InHostWorld; // a guest only shares once it plays in the host's world
        }

        private static string Read(GameSlot slot)
        {
            FsJSONManager.Serialize(new TownSnapshot { townboard = slot.townboardSaveInfo, banker = slot.bankerSaveInfo }, out fsData data);
            return data.ToString();
        }

        /// <summary>Every frame from CoopManager.</summary>
        public static void Tick()
        {
            if (Time.realtimeSinceStartup < _next) return;
            _next = Time.realtimeSinceStartup + Interval;
            if (!Ready(out var s, out var slot)) return;
            try
            {
                string json = Read(slot);
                if (json == _lastSent || json == _lastApplied) return;
                _lastSent = json;
                s.SendGame(MessageType.TownState, new TownState { Data = PackedText.Pack(json) });
            }
            catch (Exception e) { CoopPlugin.Log.LogWarning("Could not send the town: " + e.Message); _next += 10f; }
        }

        private static void Apply(string json)
        {
            if (!Ready(out _, out var slot) || string.IsNullOrEmpty(json)) return;
            try
            {
                TownSnapshot snap = null;
                FsJSONManager.Deserialize(json, ref snap, false);
                if (snap == null) return;
                if (snap.townboard != null) slot.townboardSaveInfo = snap.townboard;
                if (snap.banker != null) slot.bankerSaveInfo = snap.banker;
                _lastApplied = Read(slot);
                _lastSent = _lastApplied;
                // New shops in town (blacksmith, witch...) open straight away if we're in town.
                var village = UnityEngine.Object.FindObjectOfType<VillageManager>();
                if (village != null) village.UpdateCharacters();
                CoopPlugin.Log.LogInfo("Co-op: town upgrades updated from another player");
            }
            catch (Exception e) { CoopPlugin.Log.LogWarning("Could not apply the town: " + e.Message); }
        }
    }
}
