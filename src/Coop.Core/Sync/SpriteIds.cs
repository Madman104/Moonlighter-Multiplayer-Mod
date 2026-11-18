using System;
using System.Collections.Generic;

namespace Coop.Core.Sync
{
    /// <summary>
    /// Sender side: gives each sprite key a small id the first time it is used.
    /// The two game copies may have different sprites loaded, so ids are per session,
    /// not derived from a shared list. Id 0 = no sprite.
    /// </summary>
    public sealed class SpriteIdAllocator
    {
        private readonly Dictionary<string, ushort> _ids = new Dictionary<string, ushort>(StringComparer.Ordinal);
        private ushort _next = 1;

        public ushort GetOrAdd(string key, out bool isNew)
        {
            isNew = false;
            if (string.IsNullOrEmpty(key)) return 0;
            if (_ids.TryGetValue(key, out var id)) return id;
            if (_next == ushort.MaxValue) return 0; // table full: treat as unknown
            id = _next++;
            _ids[key] = id;
            isNew = true;
            return id;
        }

        public void Reset()
        {
            _ids.Clear();
            _next = 1;
        }
    }

    /// <summary>Receiver side: ids announced by the other player.</summary>
    public sealed class SpriteNameMap
    {
        private readonly Dictionary<ushort, string> _keys = new Dictionary<ushort, string>();

        public void Define(ushort id, string key)
        {
            if (id == 0) return;
            _keys[id] = key;
        }

        public string GetKey(ushort id)
        {
            return id != 0 && _keys.TryGetValue(id, out var k) ? k : null;
        }

        public void Clear() => _keys.Clear();
    }
}
