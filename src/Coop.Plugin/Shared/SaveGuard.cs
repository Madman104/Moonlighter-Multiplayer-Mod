using Coop.Core.Session;
using Coop.Plugin.Runtime;
using HarmonyLib;

namespace Coop.Plugin.Shared
{
    /// <summary>
    /// The joining player's own save is never written during co-op (design anti-goal): the
    /// shared world lives in the host's save. GameManager.SerializeGameSlot is the game's only
    /// writer of a save slot.
    /// </summary>
    internal static class SaveGuard
    {
        private static bool _logged;

        public static void Apply(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(GameManager), nameof(GameManager.SerializeGameSlot)),
                prefix: new HarmonyMethod(typeof(SaveGuard), nameof(SerializePrefix)));
        }

        private static bool SerializePrefix()
        {
            var s = CoopManager.Instance != null ? CoopManager.Instance.Session : null;
            bool skip = s != null && !s.IsHost && s.State == SessionState.Connected;
            if (skip && !_logged)
            {
                _logged = true;
                CoopPlugin.Log.LogInfo("Co-op: your own save is not written while you are a guest");
            }
            if (!skip) _logged = false;
            return !skip;
        }
    }
}
