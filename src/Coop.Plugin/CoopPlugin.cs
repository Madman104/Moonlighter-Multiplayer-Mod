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
        public const string ModVersion = "1.4.0";

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
            Menu.MainMenuCoop.Apply(_harmony);
            Dungeon.FloorSync.Apply(_harmony);
            Dungeon.TravelSync.Apply(_harmony);
            Dungeon.EnemySync.Apply(_harmony);
            Dungeon.EnemyHitGuard.Apply(_harmony);
            Dungeon.FloorItems.Apply(_harmony);
            Dungeon.BreakableSync.Apply(_harmony);
            Dungeon.ChestSync.Apply(_harmony);
            Sync.PartnerSounds.Apply(_harmony);
            Shared.GoldSync.Apply(_harmony);
            Shared.SaveGuard.Apply(_harmony);
            Shared.GhostMode.Apply(_harmony);
            Shared.DaySync.Apply(_harmony);
            Shared.WorldSync.Apply(_harmony);
            Shared.ShopSync.Apply(_harmony);
            Shared.CitizenMirror.Apply(_harmony);
            Runtime.CoopManager.Create();
        }
    }
}
