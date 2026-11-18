using System.Collections.Generic;

namespace Coop.Core.Dungeon
{
    /// <summary>
    /// Client side: dungeon floor layouts received from the host, waiting to be built.
    /// Keeps only the newest layout per (culture, floor); taking one removes it.
    /// </summary>
    public sealed class FloorLayoutStore
    {
        private readonly Dictionary<(int, int), string> _layouts = new Dictionary<(int, int), string>();

        public void Put(int culture, int floor, string json) => _layouts[(culture, floor)] = json;

        public bool TryTake(int culture, int floor, out string json)
        {
            if (_layouts.TryGetValue((culture, floor), out json))
            {
                _layouts.Remove((culture, floor));
                return true;
            }
            return false;
        }

        public void Clear() => _layouts.Clear();
    }
}
