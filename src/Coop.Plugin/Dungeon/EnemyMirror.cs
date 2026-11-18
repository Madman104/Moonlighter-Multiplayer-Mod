using System;
using System.Collections.Generic;
using Coop.Core.Dungeon;
using Coop.Core.Protocol;
using Coop.Core.Session;
using Coop.Core.Sync;
using Coop.Plugin.Runtime;
using Coop.Plugin.Sync;
using UnityEngine;

namespace Coop.Plugin.Dungeon
{
    /// <summary>
    /// Stage 3b: enemies are in the same place on both screens.
    ///
    /// Each enemy in the shared room is run by one game: the one whose player is closer
    /// (decided by the host every 0.5 s, with a margin; a ghost never runs enemies). The runner
    /// sends each enemy's position and sprites 10 times a second. The other game pauses that
    /// enemy's AI and movement and shows it exactly as received; hits on it still count
    /// (health is shared by EnemySync). Enemies the other game doesn't have (summons spawned by
    /// AI) are drawn there as sprite-only copies.
    /// Trade-off: an enemy only attacks the player whose game runs it, and its projectiles are
    /// only on the runner's screen.
    /// </summary>
    internal static class EnemyMirror
    {
        public const float OwnerInterval = 0.5f;
        public const float StateInterval = 0.1f;
        public const ushort SpriteBase = 50000; // above Will's and the shop customers' ids

        private sealed class Remote
        {
            public Vector3 Target;
            public EntityState State;
            public float At;
        }

        private static readonly SpriteIdAllocator Sprites = new SpriteIdAllocator(SpriteBase);
        private static readonly SpriteNameMap Names = new SpriteNameMap();
        private static readonly HashSet<ushort> GuestOwned = new HashSet<ushort>();
        private static readonly Dictionary<ushort, Remote> Incoming = new Dictionary<ushort, Remote>();
        private static readonly Dictionary<ushort, GameObject> Drawn = new Dictionary<ushort, GameObject>();
        /// <summary>What a paused enemy had before pausing, restored exactly on resume.</summary>
        private sealed class SavedState
        {
            public readonly List<KeyValuePair<Behaviour, bool>> Enabled = new List<KeyValuePair<Behaviour, bool>>();
            public readonly List<KeyValuePair<Rigidbody2D, bool>> Kinematic = new List<KeyValuePair<Rigidbody2D, bool>>();
        }

        private static readonly Dictionary<GameObject, SavedState> Paused = new Dictionary<GameObject, SavedState>();
        private static int _ownersKey = int.MinValue, _incomingKey = int.MinValue;
        private static float _nextOwners, _nextState;
        private static Vector2 _partnerPos;
        private static bool _partnerKnown, _partnerGhost;
        private static float _partnerAt;
        private static Dictionary<string, Sprite> _spriteCache;
        private static float _nextCacheRefresh;

        public static void Attach(PeerSession session)
        {
            Reset();
            Sprites.Reset();
            Names.Clear();
            session.SpriteDefReceived += def => { if (def.Id >= SpriteBase) Names.Define(def.Id, def.Key); };
            session.PlayerStateReceived += st =>
            {
                _partnerKnown = st.SceneId >= 0;
                _partnerPos = new Vector2(st.X, st.Y);
                _partnerGhost = st.Ghost;
                _partnerAt = Time.realtimeSinceStartup;
            };
            session.GameMessageReceived += OnGameMessage;
            session.StateChanged += st => { if (st == SessionState.Closed) Reset(); };
        }

        private static void Reset()
        {
            GuestOwned.Clear();
            Incoming.Clear();
            foreach (var go in Drawn.Values) if (go != null) UnityEngine.Object.Destroy(go);
            Drawn.Clear();
            ResumeAll();
            _ownersKey = _incomingKey = int.MinValue;
        }

        private static bool Connected(out PeerSession s)
        {
            s = CoopManager.Instance != null ? CoopManager.Instance.Session : null;
            return s != null && s.State == SessionState.Connected;
        }

        private static DungeonRoom CurrentRoom()
        {
            if (!SharedCultures.InSharedDungeon() || FloorSync.ClientBuilding) return null;
            return DungeonManager.Instance.currentDungeonRoom;
        }

        private static int KeyOf(DungeonRoom r) => EnemyOwnership.RoomKey(r.x, r.y, r.isSecret);

        private static void OnGameMessage(MessageType type, object msg)
        {
            if (type == MessageType.EnemyOwners)
            {
                var o = (EnemyOwners)msg;
                _ownersKey = o.Key;
                GuestOwned.Clear();
                foreach (var id in o.GuestOwned) GuestOwned.Add(id);
            }
            else if (type == MessageType.EntityStates)
            {
                var es = (EntityStates)msg;
                if (es.Group != EntityStates.Enemies) return;
                if (es.Key != _incomingKey) { Incoming.Clear(); _incomingKey = es.Key; }
                var seen = new HashSet<ushort>();
                float now = Time.realtimeSinceStartup;
                foreach (var e in es.Entities)
                {
                    seen.Add(e.Id);
                    if (!Incoming.TryGetValue(e.Id, out var r)) Incoming[e.Id] = r = new Remote();
                    r.State = e;
                    r.Target = new Vector3(e.X, e.Y, 0f);
                    r.At = now;
                }
                var gone = new List<ushort>();
                foreach (var id in Incoming.Keys) if (!seen.Contains(id)) gone.Add(id);
                foreach (var id in gone) Incoming.Remove(id);
            }
        }

        private static bool LocalRuns(PeerSession s, ushort id, int key)
        {
            bool guestRuns = key == _ownersKey && GuestOwned.Contains(id);
            return s.IsHost ? !guestRuns : guestRuns;
        }

        /// <summary>Every frame (after the game's own Update), from CoopManager.</summary>
        public static void Tick()
        {
            if (!Connected(out var s)) return;
            var room = CurrentRoom();
            if (room == null) { ClearDrawn(); ResumeAll(); return; }
            int key = KeyOf(room);
            float now = Time.realtimeSinceStartup;
            var enemies = room.GetComponentsInChildren<CoopEnemyId>(false);

            if (s.IsHost && now >= _nextOwners)
            {
                _nextOwners = now + OwnerInterval;
                DecideOwners(s, key, enemies);
            }

            bool send = now >= _nextState;
            if (send) _nextState = now + StateInterval;
            var outgoing = send ? new List<EntityState>() : null;
            var present = new HashSet<ushort>();

            foreach (var tag in enemies)
            {
                if (tag == null || tag.Id < 0 || tag.Id > ushort.MaxValue) continue;
                ushort id = (ushort)tag.Id;
                present.Add(id);
                var go = tag.gameObject;
                var enemy = go.GetComponent<Enemy>();
                if (enemy != null && enemy.enemyStats != null && !enemy.enemyStats.isAlive)
                {
                    SetPaused(go, false); // let the death animation and room clear run here
                    continue;
                }
                bool runs = LocalRuns(s, id, key);
                bool haveRemote = Incoming.TryGetValue(id, out var remote) && _incomingKey == key && now - remote.At < 1f;
                // Not run here and the runner is sending: follow it. Otherwise run it here.
                bool follow = !runs && haveRemote;
                SetPaused(go, follow);
                if (follow) Follow(go, remote);
                else if (send && runs) outgoing.Add(Describe(s, go.transform, id));
            }

            if (send) s.SendFrequent(MessageType.EntityStates, new EntityStates { Group = EntityStates.Enemies, Key = key, Entities = outgoing.ToArray() });
            DrawMissing(key, present);
        }

        private static void DecideOwners(PeerSession s, int key, CoopEnemyId[] enemies)
        {
            var will = HeroMerchant.Instance;
            if (will == null) return;
            Vector2 host = will.transform.position;
            bool hostOut = Shared.GhostMode.LocalGhost;
            bool guestIn = _partnerKnown && !_partnerGhost && Time.realtimeSinceStartup - _partnerAt < 1f;
            if (key != _ownersKey) GuestOwned.Clear();
            _ownersKey = key;
            foreach (var tag in enemies)
            {
                if (tag == null || tag.Id < 0 || tag.Id > ushort.MaxValue) continue;
                ushort id = (ushort)tag.Id;
                Vector2 p = tag.transform.position;
                float hd = hostOut ? float.PositiveInfinity : (p - host).sqrMagnitude;
                float gd = guestIn ? (p - _partnerPos).sqrMagnitude : float.PositiveInfinity;
                if (EnemyOwnership.GuestShouldOwn(GuestOwned.Contains(id), hd, gd, EnemyOwnership.DefaultMargin)) GuestOwned.Add(id);
                else GuestOwned.Remove(id);
            }
            var list = new ushort[GuestOwned.Count];
            GuestOwned.CopyTo(list);
            s.SendGame(MessageType.EnemyOwners, new EnemyOwners { Key = key, GuestOwned = list });
        }

        // ---- running side ----

        private static EntityState Describe(PeerSession s, Transform root, ushort id)
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
                    Slot = (byte)i,
                    SpriteId = sid,
                    Visible = visible && sid != 0,
                    FlipX = r.flipX,
                    SortingOrder = (short)Mathf.Clamp(r.sortingOrder, short.MinValue, short.MaxValue),
                    LocalX = local.x,
                    LocalY = local.y,
                });
            }
            var p = root.position;
            return new EntityState { Id = id, X = p.x, Y = p.y, Layers = layers.ToArray() };
        }

        // ---- following side ----

        private static readonly Dictionary<GameObject, SpriteRenderer[]> RendererCache = new Dictionary<GameObject, SpriteRenderer[]>();

        private static void Follow(GameObject go, Remote remote)
        {
            var t = go.transform;
            t.position = Vector3.Lerp(t.position, new Vector3(remote.Target.x, remote.Target.y, t.position.z),
                Mathf.Clamp01(Time.deltaTime * 15f));
            if (!RendererCache.TryGetValue(go, out var renderers) || renderers == null)
                RendererCache[go] = renderers = go.GetComponentsInChildren<SpriteRenderer>(true);
            foreach (var layer in remote.State.Layers)
            {
                if (layer.Slot >= renderers.Length || renderers[layer.Slot] == null) continue;
                var r = renderers[layer.Slot];
                if (!layer.Visible) { r.enabled = false; continue; }
                var sprite = Resolve(Names.GetKey(layer.SpriteId));
                if (sprite == null) continue; // keep the local frame rather than blank
                r.enabled = true;
                r.sprite = sprite;
                r.flipX = layer.FlipX;
            }
        }

        /// <summary>Pauses an enemy's AI, path-finding, animation and physics (or resumes it).</summary>
        private static void SetPaused(GameObject go, bool paused)
        {
            bool was = Paused.ContainsKey(go);
            if (was == paused) return;
            try
            {
                if (paused)
                {
                    var saved = new SavedState();
                    foreach (var b in go.GetComponentsInChildren<EnemyBehaviour>(true)) Pause(saved, b);
                    foreach (var a in go.GetComponentsInChildren<Animator>(true)) Pause(saved, a);
                    foreach (var c in go.GetComponentsInChildren<MonoBehaviour>(true))
                        if (c != null && c.GetType().Name.StartsWith("PolyNav", StringComparison.Ordinal)) Pause(saved, c);
                    foreach (var rb in go.GetComponentsInChildren<Rigidbody2D>(true))
                    {
                        saved.Kinematic.Add(new KeyValuePair<Rigidbody2D, bool>(rb, rb.isKinematic));
                        rb.velocity = Vector2.zero;
                        rb.isKinematic = true;
                    }
                    Paused[go] = saved;
                }
                else
                {
                    Restore(Paused[go]);
                    Paused.Remove(go);
                }
            }
            catch (Exception e) { CoopPlugin.Log.LogWarning("Could not pause/resume an enemy: " + e.Message); }
        }

        private static void Pause(SavedState saved, Behaviour b)
        {
            if (b == null) return;
            saved.Enabled.Add(new KeyValuePair<Behaviour, bool>(b, b.enabled));
            b.enabled = false;
        }

        private static void Restore(SavedState saved)
        {
            foreach (var kv in saved.Enabled) if (kv.Key != null) kv.Key.enabled = kv.Value;
            foreach (var kv in saved.Kinematic) if (kv.Key != null) kv.Key.isKinematic = kv.Value;
        }

        /// <summary>Resumes every paused enemy and forgets destroyed ones.</summary>
        private static void ResumeAll()
        {
            if (Paused.Count == 0) return;
            foreach (var kv in Paused)
                if (kv.Key != null)
                {
                    try { Restore(kv.Value); }
                    catch (Exception e) { CoopPlugin.Log.LogWarning("Could not resume an enemy: " + e.Message); }
                }
            Paused.Clear();
            RendererCache.Clear();
        }

        // ---- enemies only the runner has (summons): sprite-only copies ----

        private static void DrawMissing(int key, HashSet<ushort> present)
        {
            var keep = new HashSet<ushort>();
            if (_incomingKey == key)
            {
                float now = Time.realtimeSinceStartup;
                foreach (var kv in Incoming)
                {
                    if (present.Contains(kv.Key) || now - kv.Value.At > 1f) continue;
                    keep.Add(kv.Key);
                    if (!Drawn.TryGetValue(kv.Key, out var go) || go == null)
                    {
                        go = new GameObject("CoopEnemyCopy_" + kv.Key);
                        go.transform.position = kv.Value.Target;
                        Drawn[kv.Key] = go;
                    }
                    go.transform.position = Vector3.Lerp(go.transform.position, kv.Value.Target, Mathf.Clamp01(Time.deltaTime * 15f));
                    DrawLayers(go, kv.Value.State);
                }
            }
            var gone = new List<ushort>();
            foreach (var id in Drawn.Keys) if (!keep.Contains(id)) gone.Add(id);
            foreach (var id in gone)
            {
                if (Drawn[id] != null) UnityEngine.Object.Destroy(Drawn[id]);
                Drawn.Remove(id);
            }
        }

        private static void DrawLayers(GameObject go, EntityState st)
        {
            var like = HeroMerchant.Instance != null ? HeroMerchant.Instance.GetComponentInChildren<SpriteRenderer>() : null;
            foreach (var layer in st.Layers)
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

        private static void ClearDrawn()
        {
            if (Drawn.Count == 0) return;
            foreach (var go in Drawn.Values) if (go != null) UnityEngine.Object.Destroy(go);
            Drawn.Clear();
            RendererCache.Clear();
        }

        private static Sprite Resolve(string key)
        {
            if (key == null) return null;
            if (_spriteCache == null || (!_spriteCache.ContainsKey(key) && Time.realtimeSinceStartup >= _nextCacheRefresh))
            {
                _nextCacheRefresh = Time.realtimeSinceStartup + 2f;
                _spriteCache = new Dictionary<string, Sprite>();
                foreach (var sp in Resources.FindObjectsOfTypeAll<Sprite>())
                {
                    var k = LocalPlayerSampler.KeyOf(sp);
                    if (k != null && !_spriteCache.ContainsKey(k)) _spriteCache[k] = sp;
                }
            }
            return _spriteCache.TryGetValue(key, out var sprite) ? sprite : null;
        }
    }
}
