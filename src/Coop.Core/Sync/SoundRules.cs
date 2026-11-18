using System;
using System.Collections.Generic;

namespace Coop.Core.Sync
{
    /// <summary>How loud a partner's sound is here, and how many are sent.</summary>
    public static class SoundFalloff
    {
        /// <summary>
        /// Full volume up to near, silent from far, linear in between. Distances are in world
        /// units; the plugin scales near/far with the camera so it doesn't depend on the unit.
        /// </summary>
        public static float Volume(float distance, float near, float far)
        {
            if (float.IsNaN(distance) || far <= 0f) return 0f;
            if (distance <= near) return 1f;
            if (distance >= far || far <= near) return 0f;
            return 1f - (distance - near) / (far - near);
        }
    }

    /// <summary>
    /// Limits outgoing sounds: one of each name per MinGap seconds, and at most MaxPerSecond in
    /// total. Stops are never limited (a dropped stop would leave a looping sound playing).
    /// </summary>
    public sealed class SoundThrottle
    {
        public const float MinGap = 0.05f;
        public const int MaxPerSecond = 20;

        private readonly Dictionary<string, float> _last = new Dictionary<string, float>();
        private float _windowStart = float.NegativeInfinity;
        private int _inWindow;

        public bool Allow(string name, float now, bool stop = false)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (stop) return true;
            if (_last.TryGetValue(name, out var at) && now - at < MinGap) return false;
            if (now - _windowStart >= 1f) { _windowStart = now; _inWindow = 0; }
            if (_inWindow >= MaxPerSecond) return false;
            _inWindow++;
            _last[name] = now;
            return true;
        }

        public void Reset()
        {
            _last.Clear();
            _windowStart = float.NegativeInfinity;
            _inWindow = 0;
        }
    }
}
