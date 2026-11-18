using BepInEx;
using BepInEx.Logging;
using Coop.Plugin.Patches;
using HarmonyLib;

namespace Coop.Plugin
{
    [BepInPlugin(Guid, "Moonlighter Co-op", ModVersion)]
    public class CoopPlugin : BaseUnityPlugin
    {
        public const string Guid = "com.coop.moonlighter";
        public const string ModVersion = "0.1.0";

        internal static ManualLogSource Log;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo($"Moonlighter Co-op {ModVersion} loaded");
            CoopConfig.Bind(Config);
            // Keep simulating and networking when the window loses focus (two windows on one PC, alt-tab).
            UnityEngine.Application.runInBackground = true;
            _harmony = new Harmony(Guid);
            PlatformPatches.Apply(_harmony);
            Menu.CoopPauseMenu.Apply(_harmony);
            Dungeon.FloorSync.Apply(_harmony);
            Dungeon.TravelSync.Apply(_harmony);
            Dungeon.EnemySync.Apply(_harmony);
            Shared.GoldSync.Apply(_harmony);
            Shared.SaveGuard.Apply(_harmony);
            Runtime.CoopManager.Create();
        }
    }
}
