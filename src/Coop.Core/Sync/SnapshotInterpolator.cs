using System.Collections.Generic;

namespace Coop.Core.Sync
{
    /// <summary>
    /// Smooths a remote player's position: shows it slightly in the past (delay) and
    /// blends between the two snapshots around that time. Never extrapolates.
    /// </summary>
    public sealed class SnapshotInterpolator
    {
        public const int Capacity = 32;

        private struct Snap
        {
            public double T;
            public float X, Y, Z;
        }

        private readonly double _delay;
        private readonly List<Snap> _snaps = new List<Snap>(Capacity);

        public SnapshotInterpolator(double delaySeconds = 0.1)
        {
            _delay = delaySeconds;
        }

        public int Count => _snaps.Count;

        public void Add(double time, float x, float y, float z)
        {
            if (_snaps.Count > 0 && time <= _snaps[_snaps.Count - 1].T) return; // late or duplicate
            _snaps.Add(new Snap { T = time, X = x, Y = y, Z = z });
            if (_snaps.Count > Capacity) _snaps.RemoveAt(0);
        }

        public void Clear() => _snaps.Clear();

        public bool TrySample(double now, out float x, out float y, out float z)
        {
            x = y = z = 0;
            if (_snaps.Count == 0) return false;

            double t = now - _delay;
            var first = _snaps[0];
            var last = _snaps[_snaps.Count - 1];
            if (t <= first.T) { x = first.X; y = first.Y; z = first.Z; return true; }
            if (t >= last.T) { x = last.X; y = last.Y; z = last.Z; return true; }

            for (int i = 1; i < _snaps.Count; i++)
            {
                var b = _snaps[i];
                if (b.T < t) continue;
                var a = _snaps[i - 1];
                float k = (float)((t - a.T) / (b.T - a.T));
                x = a.X + (b.X - a.X) * k;
                y = a.Y + (b.Y - a.Y) * k;
                z = a.Z + (b.Z - a.Z) * k;
                return true;
            }
            x = last.X; y = last.Y; z = last.Z;
            return true;
        }
    }
}
