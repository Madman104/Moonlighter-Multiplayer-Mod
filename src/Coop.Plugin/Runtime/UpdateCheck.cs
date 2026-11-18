using System.Collections;
using Coop.Core.Session;
using UnityEngine;
using UnityEngine.Networking;

namespace Coop.Plugin.Runtime
{
    /// <summary>
    /// Once per start: asks GitHub for the latest release of this mod. If it is newer, a toast
    /// says so (again when a connection is refused for a version mismatch). Sends nothing but
    /// the request itself; turn off with [Updates] CheckForUpdates.
    /// </summary>
    internal static class UpdateCheck
    {
        public const string ReleasesPage = "github.com/Madman104/Moonlighter-Multiplayer-Mod/releases";
        private const string LatestApi = "https://api.github.com/repos/Madman104/Moonlighter-Multiplayer-Mod/releases/latest";

        /// <summary>The newer version on GitHub, or null.</summary>
        public static string Newer { get; private set; }

        public static void Start(MonoBehaviour host)
        {
            if (CoopConfig.CheckForUpdates.Value) host.StartCoroutine(Run());
        }

        private static IEnumerator Run()
        {
            yield return new WaitForSecondsRealtime(3f);
            using (var req = UnityWebRequest.Get(LatestApi))
            {
                req.SetRequestHeader("User-Agent", "MoonlighterCoop-update-check");
                req.SetRequestHeader("Accept", "application/vnd.github+json");
                req.timeout = 10;
                yield return req.SendWebRequest();
                if (req.isNetworkError || req.isHttpError)
                {
                    CoopPlugin.Log.LogInfo("Update check skipped: " + req.error);
                    yield break;
                }
                string tag = ModVersionText.TagFromReleaseJson(req.downloadHandler.text);
                if (!ModVersionText.IsNewer(tag, CoopPlugin.ModVersion))
                {
                    CoopPlugin.Log.LogInfo($"Co-op mod is up to date ({CoopPlugin.ModVersion}; latest {tag ?? "?"})");
                    yield break;
                }
                Newer = tag.TrimStart('v');
                CoopPlugin.Log.LogInfo($"Co-op mod {Newer} is out (you have {CoopPlugin.ModVersion}): {ReleasesPage}");
            }
            // Show it a few times while the main menu is likely up.
            for (int i = 0; i < 3; i++)
            {
                CoopToast.Show(Message());
                yield return new WaitForSecondsRealtime(5f);
            }
        }

        public static string Message() => $"Co-op mod {Newer} is out (you have {CoopPlugin.ModVersion}) - get it from {ReleasesPage}";
    }
}
