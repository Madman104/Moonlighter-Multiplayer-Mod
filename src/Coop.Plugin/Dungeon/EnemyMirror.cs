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
        public const float StateInterval = 0.066f; // 15 a second: smoother enemies on the other screens
        public const ushort SpriteBase = 50000; // above Will's and the shop customers' ids

        private sealed class Remote
        {
            public Vector3 Target;
            public EntityState State;
            public float At;
            public int From;  // player whose game runs it
            public int Key;   // room it was sent for
        }

        private sealed class Player { public Vector2 Pos; public bool Ghost; public float At; public int Scene; public int Room; }

        private static readonly SpriteIdAllocator Sprites = new SpriteIdAllocator(SpriteBase);
        // Each runner numbers their own sprites: one map per player.
        private static readonly Dictionary<int, SpriteNameMap> Names = new Dictionary<int, SpriteNameMap>();
        /// <summary>Per room (RoomKey): who runs each enemy there. A room with no entry yet is run by whoever is in it.</summary>
        private static readonly Dictionary<int, Dictionary<ushort, int>> OwnersByRoom = new Dictionary<int, Dictionary<ushort, int>>();
        private static readonly Dictionary<int, Player> Players = new Dictionary<int, Player>();
        private static readonly Dictionary<ushort, Remote> Incoming = new Dictionary<ushort, Remote>();
        private static readonly Dictionary<ushort, GameObject> Drawn = new Dictionary<ushort, GameObject>();
        /// <summary>What a paused enemy had before pausing, restored exactly on resume.</summary>
        private sealed class SavedState
        {
            public readonly List<KeyValuePair<Behaviour, bool>> Enabled = new List<KeyValuePair<Behaviour, bool>>();
            public readonly List<KeyValuePair<Rigidbody2D, bool>> Kinematic = new List<KeyValuePair<Rigidbody2D, bool>>();
        }

        private static readonly Dictionary<GameObject, SavedState> Paused = new Dictionary<GameObject, SavedState>();
        private static int _ownersKey = int.MinValue;
        private static DungeonRoom _listedRoom;
        private static CoopEnemyId[] _listed;
        private static float _nextList;
        private static readonly Dictionary<Transform, SpriteRenderer[]> DescribeCache = new Dictionary<Transform, SpriteRenderer[]>();
        private static float _nextDescribeFlush;
        private static float _nextOwners, _nextState;

        public static void Attach(CoopLink session)
        {
            Reset();
            Sprites.Reset();
            Names.Clear();
            Players.Clear();
            session.SpriteDefReceived += def => { if (def.Id >= SpriteBase) NamesOf(session.LastFrom).Define(def.Id, def.Key); };
            session.PlayerStateReceived += st =>
            {
                if (!Players.TryGetValue(session.LastFrom, out var p)) Players[session.LastFrom] = p = new Player();
                p.Pos = new Vector2(st.X, st.Y);
                p.Ghost = st.Ghost || st.SceneId < 0;
                p.Scene = st.SceneId;
                p.Room = st.RoomKey;
                p.At = Time.realtimeSinceStartup;
            };
            session.PeerLeft += i => Players.Remove(i);
            session.GameMessageReceived += OnGameMessage;
            session.StateChanged += st => { if (st == SessionState.Closed) Reset(); };
        }

        internal static SpriteNameMap NamesOf(int player)
        {
            if (!Names.TryGetValue(player, out var m)) Names[player] = m = new SpriteNameMap();
            return m;
        }

        private static void Reset()
        {
            OwnersByRoom.Clear();
            Incoming.Clear();
            foreach (var go in Drawn.Values) if (go != null) UnityEngine.Object.Destroy(go);
            Drawn.Clear();
            ResumeAll();
            _ownersKey = int.MinValue;
        }

        private static bool Connected(out CoopLink s)
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
                var map = new Dictionary<ushort, int>();
                for (int i = 0; i < o.Ids.Length && i < o.Owners.Length; i++) map[o.Ids[i]] = o.Owners[i];
                OwnersByRoom[o.Key] = map;
            }
            else if (type == MessageType.EntityStates)
            {
                var es = (EntityStates)msg;
                if (es.Group != EntityStates.Enemies) return;
                int from = CoopManager.Instance != null && CoopManager.Instance.Session != null ? CoopManager.Instance.Session.LastFrom : 0;
                var seen = new HashSet<ushort>();
                float now = Time.realtimeSinceStartup;
                foreach (var e in es.Entities)
                {
                    seen.Add(e.Id);
                    if (!Incoming.TryGetValue(e.Id, out var r)) Incoming[e.Id] = r = new Remote();
                    r.State = e;
                    r.Target = new Vector3(e.X, e.Y, 0f);
                    r.At = now;
                    r.From = from;
                    r.Key = es.Key;
                }
                // This runner no longer runs the ones it left out.
                var gone = new List<ushort>();
                foreach (var kv in Incoming) if (kv.Value.From == from && !seen.Contains(kv.Key)) gone.Add(kv.Key);
                foreach (var id in gone) Incoming.Remove(id);
            }
        }

        private static bool LocalRuns(CoopLink s, ushort id, int key)
        {
            if (!OwnersByRoom.TryGetValue(key, out var map)) return true; // no decision for this room yet: we're here, so run it
            int owner = map.TryGetValue(id, out var o) ? o : 0;
            return owner == s.LocalIndex;
        }

        /// <summary>
        /// True when this game runs the enemy now: it is in our current room and we own it (or
        /// nobody decided yet). Used for loot: only the running game rolls an enemy's loot.
        /// </summary>
        public static bool RunsHere(Enemy e)
        {
            if (e == null) return true;
            var s = CoopManager.Instance != null ? CoopManager.Instance.Session : null;
            if (s == null || s.State != SessionState.Connected) return true;
            var dm = DungeonManager.Instance;
            if (dm == null || e.enemyRoom == null || dm.currentDungeonRoom != e.enemyRoom) return false;
            var tag = e.GetComponent<CoopEnemyId>();
            if (tag == null || tag.Id < 0 || tag.Id > ushort.MaxValue) return true;
            return LocalRuns(s, (ushort)tag.Id, KeyOf(e.enemyRoom));
        }

        /// <summary>Every frame (after the game's own Update), from CoopManager.</summary>
        public static void Tick()
        {
            if (!Connected(out var s)) return;
            var room = CurrentRoom();
            if (room == null) { ClearDrawn(); ResumeAll(); return; }
            int key = KeyOf(room);
            float now = Time.realtimeSinceStartup;
            // Listing the room's enemies every frame was costly: refresh 4 times a second.
            if (room != _listedRoom || now >= _nextList || _listed == null)
            {
                _listedRoom = room;
                _nextList = now + 0.25f;
                _listed = room.GetComponentsInChildren<CoopEnemyId>(false);
            }
            var enemies = _listed;

            if (s.IsHost && now >= _nextOwners)
            {
                _nextOwners = now + OwnerInterval;
                DecideAllRooms(s, key, enemies);
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
                bool haveRemote = Incoming.TryGetValue(id, out var remote) && remote.Key == key && now - remote.At < 1f;
                // Not run here and the runner is sending: follow it. Otherwise run it here.
                bool follow = !runs && haveRemote;
                SetPaused(go, follow);
                if (follow) Follow(go, remote);
                else if (send && runs) outgoing.Add(Describe(s, go.transform, id));
            }

            if (send) s.SendFrequent(MessageType.EntityStates, new EntityStates { Group = EntityStates.Enemies, Key = key, Entities = outgoing.ToArray() });
            DrawMissing(key, present);
        }

        /// <summary>
        /// Host: players can be in different rooms of the floor. For the host's room and every
        /// room a guest is in, decide who runs each enemy (the nearest player in that room).
        /// </summary>
        private static void DecideAllRooms(CoopLink s, int hostKey, CoopEnemyId[] hostEnemies)
        {
            float now = Time.realtimeSinceStartup;
            int scene = GameInfo.CurrentSceneId();
            DecideOwners(s, hostKey, hostEnemies, true);
            var done = new HashSet<int> { hostKey };
            foreach (var kv in Players)
            {
                var p = kv.Value;
                if (kv.Key <= 0 || p.Scene != scene || now - p.At > 1f || p.Room == PlayerState.NoRoom || !done.Add(p.Room)) continue;
                var room = FloorItems.FindRoom(p.Room);
                if (room == null) continue;
                DecideOwners(s, p.Room, room.GetComponentsInChildren<CoopEnemyId>(true), false);
            }
        }

        private static void DecideOwners(CoopLink s, int key, CoopEnemyId[] enemies, bool hostHere)
        {
            var will = HeroMerchant.Instance;
            if (will == null) return;
            Vector2 host = will.transform.position;
            bool hostOut = Shared.GhostMode.LocalGhost || !hostHere;
            if (!OwnersByRoom.TryGetValue(key, out var Owners)) OwnersByRoom[key] = Owners = new Dictionary<ushort, int>();
            float now = Time.realtimeSinceStartup;
            int scene = GameInfo.CurrentSceneId();
            var ids = new List<ushort>();
            var owners = new List<byte>();
            var dist = new float[CoopLink.MaxPlayers];
            foreach (var tag in enemies)
            {
                if (tag == null || tag.Id < 0 || tag.Id > ushort.MaxValue) continue;
                var en = tag.GetComponent<Enemy>();
                if (en != null && en.enemyStats != null && !en.enemyStats.isAlive) continue;
                ushort id = (ushort)tag.Id;
                Vector2 p = tag.transform.position;
                for (int i = 0; i < dist.Length; i++) dist[i] = float.PositiveInfinity;
                if (!hostOut) dist[0] = (p - host).sqrMagnitude;
                // A ghost, a player in town/loading, or one we haven't heard from never runs enemies.
                foreach (var kv in Players)
                    if (kv.Key > 0 && kv.Key < dist.Length && !kv.Value.Ghost && kv.Value.Scene == scene && kv.Value.Room == key && now - kv.Value.At < 1f)
                        dist[kv.Key] = (p - kv.Value.Pos).sqrMagnitude;
                int current = Owners.TryGetValue(id, out var c) ? c : 0;
                int owner = EnemyOwnership.Owner(current, dist, Mathf.Max(0f, CoopConfig.EnemyHandoffDistance.Value));
                Owners[id] = owner;
                if (owner != 0) { ids.Add(id); owners.Add((byte)owner); }
            }
            s.SendGame(MessageType.EnemyOwners, new EnemyOwners { Key = key, Ids = ids.ToArray(), Owners = owners.ToArray() });
        }

        // ---- running side ----

        internal static EntityState Describe(CoopLink s, Transform root, ushort id)
        {
            float now = Time.realtimeSinceStartup;
            if (now >= _nextDescribeFlush) { _nextDescribeFlush = now + 1f; DescribeCache.Clear(); } // parts change rarely
            if (!DescribeCache.TryGetValue(root, out var renderers) || renderers == null)
                DescribeCache[root] = renderers = root.GetComponentsInChildren<SpriteRenderer>(true);
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
                    SortingLayerId = r.sortingLayerID,
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
                var sprite = Resolve(NamesOf(remote.From).GetKey(layer.SpriteId));
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

        /// <summary>
        /// True when this game only mirrors the enemy: another player's game runs it and it is
        /// chasing and attacking that player. Its hits must not land on this game's Will.
        /// </summary>
        public static bool IsFollowed(Component c)
        {
            if (c == null || Paused.Count == 0) return false;
            var tag = c.GetComponentInParent<CoopEnemyId>();
            return tag != null && Paused.ContainsKey(tag.gameObject);
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
            {
                float now = Time.realtimeSinceStartup;
                foreach (var kv in Incoming)
                {
                    if (kv.Value.Key != key || present.Contains(kv.Key) || now - kv.Value.At > 1f) continue;
                    keep.Add(kv.Key);
                    if (!Drawn.TryGetValue(kv.Key, out var go) || go == null)
                    {
                        go = new GameObject("CoopEnemyCopy_" + kv.Key);
                        go.transform.position = kv.Value.Target;
                        Drawn[kv.Key] = go;
                    }
                    go.transform.position = Vector3.Lerp(go.transform.position, kv.Value.Target, Mathf.Clamp01(Time.deltaTime * 15f));
                    DrawLayers(go, kv.Value);
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

        private static void DrawLayers(GameObject go, Remote remote) => DrawLayers(go, remote.State, remote.From);

        /// <summary>Draws a sprite-only copy of an entity as the runner described it.</summary>
        internal static void DrawLayers(GameObject go, EntityState st, int from)
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
                var sprite = layer.Visible ? Resolve(NamesOf(from).GetKey(layer.SpriteId)) : null;
                r.enabled = sprite != null;
                if (sprite == null) continue;
                r.sprite = sprite;
                r.flipX = layer.FlipX;
                r.sortingOrder = layer.SortingOrder;
                if (layer.SortingLayerId != 0) r.sortingLayerID = layer.SortingLayerId;
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

        internal static Sprite Resolve(string key) => Sync.SpriteLookup.Get(key);
    }
}
