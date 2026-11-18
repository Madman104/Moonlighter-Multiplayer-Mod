using Coop.Core.Session;
using Coop.Plugin.Runtime;
using HarmonyLib;

namespace Coop.Plugin.Shared
{
    /// <summary>
    /// The joining player's own save is never written during co-op (design anti-goal): the
    /// shared world lives in the host's save. GameManager.SerializeGameSlot is the game's only
    /// writer of a save slot. After a guest disconnects, the in-memory game still holds co-op
    /// state (host's gold, day...), so saving stays blocked until a save is loaded again.
    /// Returning to the main menu ends the co-op session.
    /// </summary>
    internal static class SaveGuard
    {
        private static bool _guestStateLoaded;
        private static bool _logged;

        public static void Apply(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(GameManager), nameof(GameManager.SerializeGameSlot)),
                prefix: new HarmonyMethod(typeof(SaveGuard), nameof(SerializePrefix)));
            harmony.Patch(AccessTools.Method(typeof(HeroMerchantStats), nameof(HeroMerchantStats.Init)),
                postfix: new HarmonyMethod(typeof(SaveGuard), nameof(FreshLoadPostfix)));
            harmony.Patch(AccessTools.Method(typeof(GameManager), nameof(GameManager.ReturnToMainMenu)),
                prefix: new HarmonyMethod(typeof(SaveGuard), nameof(MainMenuPrefix)));
        }

        private static bool SerializePrefix()
        {
            var s = CoopManager.Instance != null ? CoopManager.Instance.Session : null;
            if (s != null && !s.IsHost && s.State == SessionState.Connected) _guestStateLoaded = true;
            if (!_guestStateLoaded) return true;
            if (!_logged)
            {
                _logged = true;
                CoopPlugin.Log.LogInfo("Co-op: your own save is not written while (or after) playing as a guest; reload your save to play solo");
            }
            return false;
        }

        // A save was (re)loaded: the in-memory game is this player's own again.
        private static void FreshLoadPostfix()
        {
            var s = CoopManager.Instance != null ? CoopManager.Instance.Session : null;
            if (s == null || s.State != SessionState.Connected) { _guestStateLoaded = false; _logged = false; }
        }

        private static void MainMenuPrefix()
        {
            if (CoopManager.Instance != null && CoopManager.Instance.IsActive)
            {
                CoopPlugin.Log.LogInfo("Co-op: leaving the session (returned to main menu)");
                CoopManager.Instance.Leave();
            }
        }

        /// <summary>Called when a session connects as guest.</summary>
        public static void MarkGuest() => _guestStateLoaded = true;
    }
}
