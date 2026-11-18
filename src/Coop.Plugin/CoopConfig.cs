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
        public static ConfigEntry<bool> ShowNames;
        public static ConfigEntry<float> PartnerSoundVolume;
        public static ConfigEntry<float> PartnerSoundRange;
        public static ConfigEntry<float> EnemyHandoffDistance;
        public static ConfigEntry<bool> CheckForUpdates;

        public static void Bind(ConfigFile cfg)
        {
            Port = cfg.Bind("Connection", "Port", 27460,
                "TCP port for direct-IP co-op (only used without Steam, or for the local test client).");
            LastHostAddress = cfg.Bind("Connection", "LastHostAddress", "127.0.0.1",
                "Address last used in Join (IP).");
            WindowKey = cfg.Bind("UI", "WindowKey", KeyCode.F8, "Key that opens the co-op window.");
            JoinedWorlds = cfg.Bind("Co-op", "JoinedWorlds", "",
                "Hosts' worlds this player has joined before (save seeds). A world not listed asks: tutorial or skip.");
            ShowNames = cfg.Bind("UI", "ShowPlayerNames", true,
                "Show the other players' names (coloured by health) above their heads. Also in pause > CO-OP.");
            PartnerSoundVolume = cfg.Bind("Sound", "PartnerSoundVolume", 1f,
                new ConfigDescription("Volume of the other players' footsteps, swings and hits (0 = off). Also in pause > CO-OP.",
                    new AcceptableValueRange<float>(0f, 1f)));
            PartnerSoundRange = cfg.Bind("Sound", "PartnerSoundRange", 3f,
                new ConfigDescription("How far partner sounds carry, in half-screens (full volume within one).",
                    new AcceptableValueRange<float>(1.5f, 10f)));
            EnemyHandoffDistance = cfg.Bind("Co-op", "EnemyHandoffDistance", 24f,
                new ConfigDescription("Host only: how much closer another player must be before an enemy turns on them (pixels; 16 = one tile).",
                    new AcceptableValueRange<float>(0f, 200f)));
            CheckForUpdates = cfg.Bind("Updates", "CheckForUpdates", true,
                "On start, look up the latest release on GitHub and say so on the main menu if there's a newer one.");
        }
    }
}
