using Coop.Plugin.TestMode;
using DigitalSunGames.Platforms;
using HarmonyLib;

namespace Coop.Plugin.Patches
{
    /// <summary>
    /// In test-client mode, run the game on its DRM-free platform layer with our isolated
    /// save storage, and never start the Steam API (the host copy already owns it).
    /// </summary>
    internal static class PlatformPatches
    {
        public static void Apply(Harmony harmony)
        {
            if (!TestClientMode.Active) return;

            harmony.Patch(AccessTools.Method(typeof(Platform), nameof(Platform.Init)),
                prefix: new HarmonyMethod(typeof(PlatformPatches), nameof(InitPrefix)));
            harmony.Patch(AccessTools.Method(typeof(SteamManager), "Awake"),
                prefix: new HarmonyMethod(typeof(PlatformPatches), nameof(SteamAwakePrefix)));
            CoopPlugin.Log.LogInfo("Test client mode: Steam disabled, saves go to CoopTestClient\\");
        }

        private static bool InitPrefix()
        {
            var platform = AccessTools.Property(typeof(Platform), "Instance").GetValue(null, null);
            var t = Traverse.Create(platform);
            if (t.Field("m_Initialized").GetValue<bool>()) return false;

            t.Field("m_Controller").SetValue(new DRMFreeManager());
            t.Field("m_CloudSave").SetValue(new TestSaveController());
            t.Field("m_Achievements").SetValue(new NoAchievements());
            t.Field("m_FileSystem").SetValue(new StandaloneFileSystemController());
            t.Field("m_StoreController").SetValue(new NoStore());
            t.Field("m_DLC").SetValue(new DRMFreeDLCController());
            Platform.CurrentPlatform = SupportedPlatforms.DRM_FREE;
            t.Field("m_Initialized").SetValue(true);
            CoopPlugin.Log.LogInfo("Test client mode: platform initialized as DRM-free");
            return false;
        }

        private static bool SteamAwakePrefix() => false;
    }
}
