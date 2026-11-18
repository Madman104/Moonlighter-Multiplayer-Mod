using System.Linq;
using Coop.Core.Sync;
using DigitalSunGames.Platforms;
using UnityEngine;

namespace Coop.Plugin.Runtime
{
    internal static class GameInfo
    {
        public static string GameVersion => Application.version;

        /// <summary>Internal ids of the DLC this copy of the game owns.</summary>
        public static string[] OwnedDlcNames()
        {
            if (Platform.DLC == null) return new string[0];
            return Platform.DLC.GetLoadedDLCs(h => h.isOwned)
                .Where(h => h.info != null)
                .Select(h => h.info.internalId)
                .Distinct()
                .ToArray();
        }

        /// <summary>
        /// Where the local Will is (see LocationId). The Will itself is never moved between
        /// scenes, so this comes from which game scenes are loaded.
        /// </summary>
        public static int CurrentSceneId()
        {
            var gm = GameManager.Instance;
            if (gm == null) return LocationId.None;
            return LocationId.From(HeroMerchant.Instance != null, gm.IsTownSceneLoaded(), gm.IsDungeonSceneLoaded());
        }
    }
}
