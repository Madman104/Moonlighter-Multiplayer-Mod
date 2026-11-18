using System;
using System.Collections.Generic;
using Coop.Core.Protocol;
using Coop.Core.Session;
using Coop.Core.Sync;
using Coop.Plugin.Runtime;
using Coop.Plugin.Sync;
using HarmonyLib;
using UnityEngine;

namespace Coop.Plugin.Shared
{
    /// <summary>
    /// Town NPCs (citizens) run in the host's game only, so both players see the same people in
    /// the same places. Both games pick the same citizens (generation is seeded from the shared
    /// world and day). The guest's citizens stop walking and follow the host's position and
    /// sprites; they stay real, so the guest can still talk to them. A citizen only the host
    /// has is drawn as a copy for the guest; one only the guest has is hidden.
    /// </summary>
    internal static class CitizenMirror
    {
        public const float StateInterval = 0.1f;
        public const ushort SpriteBase = 60000; // above Will's, customers' and enemies' sprite ids

        private sealed class Saved
        {
            public readonly List<KeyValuePair<Behaviour, bool>> Enabled = new List<KeyValuePair<Behaviour, bool>>();
            public readonly List<KeyValuePair<Renderer, bool>> Shown = new List<KeyValuePair<Renderer, bool>>();
            public readonly List<KeyValuePair<Collider2D, bool>> Colliders = new List<KeyValuePair<Collider2D, bool>>();
        }

        private static readonly SpriteIdAllocator Sprites = new SpriteIdAllocator(SpriteBase);
        private static readonly SpriteNameMap Names = new SpriteNameMap();
        private static readonly Dictionary<ushort, EntityState> Incoming = new Dictionary<ushort, EntityState>();
        private static readonly Dictionary<Citizen, Saved> Paused = new Dictionary<Citizen, Saved>();
        private static readonly Dictionary<ushort, GameObject> Copies = new Dictionary<ushort, GameObject>();
        private static float _nextSend, _lastIncoming;

        public static void Apply(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(CitizenGenerator), nameof(CitizenGenerator.InitializeAndGenerateCitizens)),
                prefix: new HarmonyMethod(typeof(CitizenMirror), nameof(GeneratePrefix)),
                postfix: new HarmonyMethod(typeof(CitizenMirror), nameof(GeneratePostfix)));
        }

        public static void Attach(CoopLink session)
        {
            Reset();
            Sprites.Reset();
            Names.Clear();
            session.SpriteDefReceived += def => { if (session.LastFrom == 0 && def.Id >= SpriteBase) Names.Define(def.Id, def.Key); };
            session.GameMessageReceived += (type, msg) =>
            {
                if (type != MessageType.EntityStates) return;
                var es = (EntityStates)msg;
                if (es.Group != EntityStates.Citizens) return;
                Incoming.Clear();
                foreach (var e in es.Entities) Incoming[e.Id] = e;
                _lastIncoming = Time.realtimeSinceStartup;
            };
            session.StateChanged += st => { if (st == SessionState.Closed) Reset(); };
        }

        private static void Reset()
        {
            foreach (var kv in Paused) if (kv.Key != null) Restore(kv.Value);
            Paused.Clear();
            foreach (var go in Copies.Values) if (go != null) UnityEngine.Object.Destroy(go);
            Copies.Clear();
            Incoming.Clear();
        }

        private static bool Connected(out CoopLink s)
        {
            s = CoopManager.Instance != null ? CoopManager.Instance.Session : null;
            return s != null && s.State == SessionState.Connected;
        }

        // ---- same citizens on both games ----

        private sealed class SeedState { public bool Seeded; public UnityEngine.Random.State Saved; }

        private static void GeneratePrefix(DayTime currentTime, out SeedState __state)
        {
            __state = new SeedState();
            if (!Connected(out var s) || (!s.IsHost && !WorldSync.InHostWorld && !WorldSync.LoadingHostWorld)) return;
            var gm = GameManager.Instance;
            if (gm == null || gm.currentGameSlot == null) return;
            __state.Saved = UnityEngine.Random.state;
            __state.Seeded = true;
            // Both games are in the same world (same slot seed) on the same day and time of day
            // (DaySync). If the guest's day is briefly behind, the sets differ and the guest falls
            // back to hiding its own extras and drawing the host's.
            int day = MoonlighterCalendar.Instance != null ? MoonlighterCalendar.TotalDays : 0;
            UnityEngine.Random.InitState(unchecked(gm.currentGameSlot.seed * 31 + day * 977 + (int)currentTime * 7919 + 4243));
        }

        private static void GeneratePostfix(SeedState __state)
        {
            if (__state != null && __state.Seeded) UnityEngine.Random.state = __state.Saved;
        }

        /// <summary>Same number on both games for the same citizen (they spawn at most once each).</summary>
        private static ushort IdOf(Citizen c)
        {
            unchecked
            {
                int h = 17;
                foreach (char ch in c.gameObject.name) h = h * 31 + ch;
                return (ushort)((h & 0x7FFF) + 1);
            }
        }

        // ---- every frame ----

        public static void Tick()
        {
            if (!Connected(out var s)) return;
            var gm = GameManager.Instance;
            bool inTown = gm != null && gm.IsTownSceneLoaded() && !gm.IsDungeonSceneLoaded() && HeroMerchant.Instance != null;
            if (!inTown || (!s.IsHost && !WorldSync.InHostWorld)) { if (Paused.Count > 0 || Copies.Count > 0) Reset(); return; }

            var citizens = UnityEngine.Object.FindObjectsOfType<Citizen>();
            if (s.IsHost) { HostSend(s, citizens); return; }

            // Guest: nothing from the host lately (host indoors or in the shop): run our own.
            if (Time.realtimeSinceStartup - _lastIncoming > 1.5f) { if (Paused.Count > 0 || Copies.Count > 0) Reset(); return; }

            var local = new HashSet<ushort>();
            foreach (var c in citizens)
            {
                if (c == null) continue;
                ushort id = IdOf(c);
                local.Add(id);
                if (Incoming.TryGetValue(id, out var st))
                {
                    Pause(c, hide: false);
                    Follow(c.transform, st);
                }
                else Pause(c, hide: true); // not in the host's town: don't show it
            }
            DrawCopies(local);
        }

        private static void HostSend(CoopLink s, Citizen[] citizens)
        {
            float now = Time.realtimeSinceStartup;
            if (now < _nextSend) return;
            _nextSend = now + StateInterval;
            var list = new List<EntityState>();
            foreach (var c in citizens)
                if (c != null && c.gameObject.activeInHierarchy) list.Add(Describe(s, c.transform, IdOf(c)));
            s.SendFrequent(MessageType.EntityStates, new EntityStates { Group = EntityStates.Citizens, Entities = list.ToArray() });
        }

        private static EntityState Describe(CoopLink s, Transform root, ushort id)
        {
            var renderers = root.GetComponentsInChildren<SpriteRenderer>(true);
            var layers = new List<SpriteLayer>();
            for (int i = 0; i < renderers.Length && i < 255; i++)
            {
                var r = renderers[i];
                if (r == null) continue;
                bool visible = r.sprite != null && r.enabled && r.gameObject.activeInHierarchy;
                ushort sid = 0;
                if (visible)
                {
                    string k = LocalPlayerSampler.KeyOf(r.sprite);
                    sid = Sprites.GetOrAdd(k, out bool isNew);
                    if (isNew && sid != 0) s.SendSpriteDef(new SpriteDef { Id = sid, Key = k });
                }
                var local = r.transform.position - root.position;
                layers.Add(new SpriteLayer
                {
                    Slot = (byte)i, SpriteId = sid, Visible = visible && sid != 0, FlipX = r.flipX,
                    SortingOrder = (short)Mathf.Clamp(r.sortingOrder, short.MinValue, short.MaxValue),
                    LocalX = local.x, LocalY = local.y,
                });
            }
            var p = root.position;
            return new EntityState { Id = id, X = p.x, Y = p.y, Layers = layers.ToArray() };
        }

        // ---- guest side ----

        private static void Pause(Citizen c, bool hide)
        {
            if (!Paused.TryGetValue(c, out var saved))
            {
                saved = new Saved();
                foreach (var b in c.GetComponentsInChildren<Behaviour>(true))
                {
                    if (b == null || b is Citizen) continue; // keep it talkable
                    string n = b.GetType().Name;
                    if (b is Animator || n.StartsWith("PolyNav", StringComparison.Ordinal) || b is Agent)
                    {
                        saved.Enabled.Add(new KeyValuePair<Behaviour, bool>(b, b.enabled));
                        b.enabled = false;
                    }
                }
                foreach (var r in c.GetComponentsInChildren<Renderer>(true)) saved.Shown.Add(new KeyValuePair<Renderer, bool>(r, r.enabled));
                foreach (var col in c.GetComponentsInChildren<Collider2D>(true)) saved.Colliders.Add(new KeyValuePair<Collider2D, bool>(col, col.enabled));
                Paused[c] = saved;
            }
            foreach (var kv in saved.Shown) if (kv.Key != null && hide) kv.Key.enabled = false;
            foreach (var kv in saved.Colliders) if (kv.Key != null) kv.Key.enabled = !hide && kv.Value;
        }

        private static void Restore(Saved saved)
        {
            foreach (var kv in saved.Enabled) if (kv.Key != null) kv.Key.enabled = kv.Value;
            foreach (var kv in saved.Shown) if (kv.Key != null) kv.Key.enabled = kv.Value;
            foreach (var kv in saved.Colliders) if (kv.Key != null) kv.Key.enabled = kv.Value;
        }

        private static void Follow(Transform t, EntityState st)
        {
            t.position = Vector3.Lerp(t.position, new Vector3(st.X, st.Y, t.position.z), Mathf.Clamp01(Time.deltaTime * 15f));
            var renderers = t.GetComponentsInChildren<SpriteRenderer>(true);
            foreach (var layer in st.Layers)
            {
                if (layer.Slot >= renderers.Length || renderers[layer.Slot] == null) continue;
                var r = renderers[layer.Slot];
                if (!layer.Visible) { r.enabled = false; continue; }
                var sprite = Resolve(Names.GetKey(layer.SpriteId));
                if (sprite == null) continue;
                r.enabled = true;
                r.sprite = sprite;
                r.flipX = layer.FlipX;
            }
        }

        private static void DrawCopies(HashSet<ushort> local)
        {
            var keep = new HashSet<ushort>();
            var like = HeroMerchant.Instance != null ? HeroMerchant.Instance.GetComponentInChildren<SpriteRenderer>() : null;
            foreach (var kv in Incoming)
            {
                if (local.Contains(kv.Key)) continue;
                keep.Add(kv.Key);
                if (!Copies.TryGetValue(kv.Key, out var go) || go == null)
                {
                    go = new GameObject("CoopCitizenCopy_" + kv.Key);
                    go.transform.position = new Vector3(kv.Value.X, kv.Value.Y, 0f);
                    Copies[kv.Key] = go;
                }
                go.transform.position = Vector3.Lerp(go.transform.position, new Vector3(kv.Value.X, kv.Value.Y, 0f), Mathf.Clamp01(Time.deltaTime * 15f));
                foreach (var layer in kv.Value.Layers)
                {
                    var name = "slot" + layer.Slot;
                    var child = go.transform.Find(name);
                    if (child == null)
                    {
                        child = new GameObject(name).transform;
                        child.SetParent(go.transform, false);
                        var nr = child.gameObject.AddComponent<SpriteRenderer>();
                        if (like != null) { nr.sortingLayerID = like.sortingLayerID; nr.sharedMaterial = like.sharedMaterial; }
                    }
                    var r = child.GetComponent<SpriteRenderer>();
                    var sprite = layer.Visible ? Resolve(Names.GetKey(layer.SpriteId)) : null;
                    r.enabled = sprite != null;
                    if (sprite == null) continue;
                    r.sprite = sprite;
                    r.flipX = layer.FlipX;
                    r.sortingOrder = layer.SortingOrder;
                    child.localPosition = new Vector3(layer.LocalX, layer.LocalY, 0f);
                }
            }
            var gone = new List<ushort>();
            foreach (var id in Copies.Keys) if (!keep.Contains(id)) gone.Add(id);
            foreach (var id in gone)
            {
                if (Copies[id] != null) UnityEngine.Object.Destroy(Copies[id]);
                Copies.Remove(id);
            }
        }

        private static Sprite Resolve(string key) => Sync.SpriteLookup.Get(key);
    }
}
