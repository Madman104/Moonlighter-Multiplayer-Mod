using System.Linq;
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
        /// Build index of the scene the local Will is in (town or dungeon), or -1 when there
        /// is no Will (main menu, loading).
        /// </summary>
        public static int CurrentSceneId()
        {
            var will = HeroMerchant.Instance;
            if (will == null) return -1;
            return will.gameObject.scene.buildIndex;
        }
    }
}
