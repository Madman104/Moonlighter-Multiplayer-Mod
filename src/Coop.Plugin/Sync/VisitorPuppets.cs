using System.Collections.Generic;
using Coop.Core.Protocol;
using Coop.Core.Session;
using Coop.Core.Sync;
using Coop.Plugin.Runtime;
using UnityEngine;

namespace Coop.Plugin.Sync
{
    /// <summary>
    /// Guest: drawn-only copies of the host's shop customers (sprites and position, no logic,
    /// no colliders). Each update lists every customer; ones not listed are removed.
    /// </summary>
    internal static class VisitorPuppets
    {
        private const float SpriteCacheRefreshSeconds = 2f;

        private sealed class Puppet
        {
            public GameObject Root;
            public readonly List<SpriteRenderer> Slots = new List<SpriteRenderer>();
            public Vector3 Target;
        }

        private static readonly SpriteNameMap Names = new SpriteNameMap();
        private static readonly Dictionary<ushort, Puppet> Puppets = new Dictionary<ushort, Puppet>();
        private static Dictionary<string, Sprite> _spriteCache;
        private static float _nextCacheRefresh;
        private static readonly HashSet<string> Missing = new HashSet<string>();

        public static void Attach(CoopLink session)
        {
            Clear();
            Names.Clear();
            session.SpriteDefReceived += def => { if (session.LastFrom == 0 && def.Id >= Shared.ShopSync.VisitorSpriteBase) Names.Define(def.Id, def.Key); };
            session.StateChanged += st => { if (st == SessionState.Closed) Clear(); };
        }

        public static void Clear()
        {
            foreach (var p in Puppets.Values) if (p.Root != null) Object.Destroy(p.Root);
            Puppets.Clear();
        }

        public static void Show(EntityStates states)
        {
            var seen = new HashSet<ushort>();
            var will = HeroMerchant.Instance;
            var willRenderer = will != null ? will.GetComponentInChildren<SpriteRenderer>() : null;
            foreach (var e in states.Entities)
            {
                seen.Add(e.Id);
                if (!Puppets.TryGetValue(e.Id, out var p) || p.Root == null)
                {
                    p = new Puppet { Root = new GameObject("CoopVisitor_" + e.Id) };
                    p.Root.transform.position = new Vector3(e.X, e.Y, 0f);
                    p.Root.AddComponent<PuppetMover>().Owner = p.Root.transform;
                    Puppets[e.Id] = p;
                }
                p.Root.GetComponent<PuppetMover>().Target = new Vector3(e.X, e.Y, 0f);
                foreach (var r in p.Slots) r.enabled = false;
                foreach (var layer in e.Layers)
                {
                    var r = SlotRenderer(p, layer.Slot, willRenderer);
                    var sprite = Resolve(Names.GetKey(layer.SpriteId));
                    r.enabled = sprite != null;
                    if (sprite == null) continue;
                    r.sprite = sprite;
                    r.flipX = layer.FlipX;
                    r.sortingOrder = layer.SortingOrder;
                    r.transform.localPosition = new Vector3(layer.LocalX, layer.LocalY, 0f);
                }
            }
            var gone = new List<ushort>();
            foreach (var kv in Puppets) if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var id in gone)
            {
                if (Puppets[id].Root != null) Object.Destroy(Puppets[id].Root);
                Puppets.Remove(id);
            }
        }

        private static SpriteRenderer SlotRenderer(Puppet p, int slot, SpriteRenderer like)
        {
            while (p.Slots.Count <= slot)
            {
                var child = new GameObject("slot" + p.Slots.Count);
                child.transform.SetParent(p.Root.transform, false);
                var r = child.AddComponent<SpriteRenderer>();
                if (like != null)
                {
                    r.sortingLayerID = like.sortingLayerID;
                    r.sharedMaterial = like.sharedMaterial;
                }
                p.Slots.Add(r);
            }
            return p.Slots[slot];
        }

        private static Sprite Resolve(string key)
        {
            if (key == null) return null;
            if (_spriteCache == null || (!_spriteCache.ContainsKey(key) && Time.realtimeSinceStartup >= _nextCacheRefresh))
            {
                _nextCacheRefresh = Time.realtimeSinceStartup + SpriteCacheRefreshSeconds;
                _spriteCache = new Dictionary<string, Sprite>();
                foreach (var s in Resources.FindObjectsOfTypeAll<Sprite>())
                {
                    var k = LocalPlayerSampler.KeyOf(s);
                    if (k != null && !_spriteCache.ContainsKey(k)) _spriteCache[k] = s;
                }
            }
            if (_spriteCache.TryGetValue(key, out var sprite)) return sprite;
            if (Missing.Add(key) && Missing.Count <= 5) CoopPlugin.Log.LogInfo("Customer sprite not loaded here: " + key);
            return null;
        }

        /// <summary>Glides a puppet toward its latest position (updates arrive ~10 times a second).</summary>
        private sealed class PuppetMover : MonoBehaviour
        {
            public Transform Owner;
            public Vector3 Target;

            private void LateUpdate()
            {
                if (Owner == null) return;
                Owner.position = Vector3.Lerp(Owner.position, Target, Mathf.Clamp01(Time.unscaledDeltaTime * 15f));
            }
        }
    }
}
