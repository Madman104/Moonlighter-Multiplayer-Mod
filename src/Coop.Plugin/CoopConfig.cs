using BepInEx.Configuration;
using UnityEngine;

namespace Coop.Plugin
{
    internal static class CoopConfig
    {
        public static ConfigEntry<int> Port;
        public static ConfigEntry<string> LastHostAddress;
        public static ConfigEntry<KeyCode> WindowKey;
        public static ConfigEntry<string> JoinedWorlds;

        public static void Bind(ConfigFile cfg)
        {
            Port = cfg.Bind("Connection", "Port", 27460,
                "TCP port for direct-IP co-op (only used without Steam, or for the local test client).");
            LastHostAddress = cfg.Bind("Connection", "LastHostAddress", "127.0.0.1",
                "Address last used in Join (IP).");
            WindowKey = cfg.Bind("UI", "WindowKey", KeyCode.F8, "Key that opens the co-op window.");
            JoinedWorlds = cfg.Bind("Co-op", "JoinedWorlds", "",
                "Hosts' worlds this player has joined before (save seeds). A world not listed asks: tutorial or skip.");
        }
    }
}
