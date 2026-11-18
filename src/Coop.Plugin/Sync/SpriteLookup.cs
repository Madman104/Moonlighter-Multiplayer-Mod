using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Coop.Plugin.Sync
{
    /// <summary>
    /// Finds a loaded sprite by its key (texture/name), for everything that draws another game's
    /// sprites (players, enemies, projectiles, townspeople, customers).
    /// Listing every loaded sprite is slow (it caused hitches), so it is done once, then again
    /// only after a scene load or when a key is missing, and at most every RebuildSeconds.
    /// </summary>
    internal static class SpriteLookup
    {
        public const float RebuildSeconds = 8f;

        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();
        private static readonly HashSet<string> Missing = new HashSet<string>();
        private static float _nextRebuild;
        private static bool _built, _hooked, _sceneChanged;

        public static Sprite Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (!_hooked)
            {
                _hooked = true;
                SceneManager.sceneLoaded += (s, m) => _sceneChanged = true;
            }
            if (Cache.TryGetValue(key, out var sprite))
            {
                if (sprite != null) return sprite;
                Cache.Remove(key); // unloaded
            }
            float now = Time.realtimeSinceStartup;
            bool mayRebuild = !_built || ((_sceneChanged || !Missing.Contains(key)) && now >= _nextRebuild);
            if (mayRebuild)
            {
                Rebuild(now);
                if (Cache.TryGetValue(key, out sprite) && sprite != null) return sprite;
            }
            Missing.Add(key);
            return null;
        }

        private static void Rebuild(float now)
        {
            _built = true;
            _sceneChanged = false;
            _nextRebuild = now + RebuildSeconds;
            Cache.Clear();
            Missing.Clear();
            foreach (var s in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                var k = LocalPlayerSampler.KeyOf(s);
                if (k != null && !Cache.ContainsKey(k)) Cache[k] = s;
            }
        }
    }
}
