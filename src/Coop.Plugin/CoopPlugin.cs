using BepInEx;
using BepInEx.Logging;

namespace Coop.Plugin
{
    [BepInPlugin(Guid, "Moonlighter Co-op", ModVersion)]
    public class CoopPlugin : BaseUnityPlugin
    {
        public const string Guid = "com.coop.moonlighter";
        public const string ModVersion = "0.1.0";

        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo($"Moonlighter Co-op {ModVersion} loaded");
        }
    }
}
