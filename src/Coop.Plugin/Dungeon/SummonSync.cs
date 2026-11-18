using System;
using System.Collections.Generic;
using Coop.Core.Dungeon;
using Coop.Core.Protocol;
using Coop.Core.Session;
using Coop.Plugin.Runtime;
using UnityEngine;

namespace Coop.Plugin.Dungeon
{
    /// <summary>
    /// Enemies summoned by other enemies (slime splits, seeder sprouts...) are real enemies on
    /// every game: the summoning game gives it an id from its own block (SummonIds) and sends
    /// what it spawned; the others spawn the same enemy with that id. From then on it is an
    /// ordinary shared enemy (health, position, ownership). If it vanishes without dying in the
    /// summoner's game, the others remove theirs.
    /// Bosses that make minions without the room's spawn call (e.g. the Golem King's rocks) are
    /// caught by a scan: an untagged, living enemy that wasn't in the room when we first saw it.
    /// Those are sent with Tile = InstantiateTile (spawned from the prefab by name).
    /// </summary>
    internal static class SummonSync
    {
        private static readonly Dictionary<long, Enemy> Local = new Dictionary<long, Enemy>();
        private static int _counter;
        private static bool _remoteSpawn;
        private static int _remoteId;
        public const int InstantiateTile = -1;
        public const int InstantiateTileNotInRoomList = -2;
        private const float ScanInterval = 0.25f;
        private static float _nextScan;
        private static DungeonRoom _scannedRoom;
        private static readonly HashSet<Enemy> Native = new HashSet<Enemy>();

        public static void Attach(CoopLink session)
        {
            Local.Clear();
            Native.Clear();
            _scannedRoom = null;
            _counter = 0;
            session.GameMessageReceived += (type, msg) =>
            {
                if (type == MessageType.SummonSpawned)
                {
                    var m = (SummonSpawned)msg;
                    int scene = GameInfo.CurrentSceneId();
                    LateApply.Run("summon " + m.Name, () => GameInfo.CurrentSceneId() != scene || TrySpawn(m), LateApply.UntilEntered);
                }
                else if (type == MessageType.SummonGone) OnGone((SummonGone)msg);
            };
            session.StateChanged += st => { if (st == SessionState.Closed) Local.Clear(); };
        }

        private static bool Sharing(out CoopLink s)
        {
            s = CoopManager.Instance != null ? CoopManager.Instance.Session : null;
            return s != null && s.State == SessionState.Connected && SharedCultures.InSharedDungeon();
        }

        private static long Key(int roomKey, int id) => ((long)roomKey << 8) | (uint)(id & 0xFF);

        /// <summary>From EnemySync: an AI spawn. Returns true when it was tagged here.</summary>
        public static bool OnSpawned(DungeonRoom room, Enemy enemy, DungeonRoomInfo.EnemySpawn spawn, int tile)
        {
            if (_remoteSpawn)
            {
                enemy.gameObject.AddComponent<CoopEnemyId>().Id = _remoteId;
                return true;
            }
            if (!Sharing(out var s) || spawn == null || spawn.enemy == null || string.IsNullOrEmpty(spawn.enemy.name)) return false;
            int id = SummonIds.For(s.LocalIndex, _counter++);
            enemy.gameObject.AddComponent<CoopEnemyId>().Id = id;
            int roomKey = EnemyOwnership.RoomKey(room.x, room.y, room.isSecret);
            Local[Key(roomKey, id)] = enemy;
            var p = enemy.transform.position;
            s.SendGame(MessageType.SummonSpawned, new SummonSpawned { RoomKey = roomKey, Id = (byte)id, Name = spawn.enemy.name, Tile = tile, X = p.x, Y = p.y });
            return true;
        }

        private static bool TrySpawn(SummonSpawned m)
        {
            if (!Sharing(out _)) return true;
            var room = FloorItems.FindRoom(m.RoomKey);
            if (room == null || !LateApply.IsCurrent(room)) return false; // spawned when we walk in
            foreach (var t in room.GetComponentsInChildren<CoopEnemyId>(true))
                if (t.Id == m.Id && t.GetComponent<Enemy>() != null && t.GetComponent<Enemy>().enemyStats != null
                    && t.GetComponent<Enemy>().enemyStats.isAlive) return true; // already have it
            if (Adopt(room, m)) return true;
            _remoteSpawn = true;
            _remoteId = m.Id;
            try
            {
                if (m.Tile < 0) { SpawnByPrefab(room, m); return true; }
                var spawn = new DungeonRoomInfo.EnemySpawn { index = m.Tile, enemy = new PrefabSpawnInfo { name = m.Name } };
                var e = room.SpawnEnemy(spawn, m.Tile);
                if (e != null) e.transform.position = new Vector3(m.X, m.Y, e.transform.position.z);
                else CoopPlugin.Log.LogWarning("Could not spawn shared summon " + m.Name);
            }
            catch (Exception e) { CoopPlugin.Log.LogWarning("Could not spawn shared summon " + m.Name + ": " + e.Message); }
            finally { _remoteSpawn = false; }
            return true;
        }

        /// <summary>
        /// Both games made the same enemy (a boss phase both saw happen): keep one id. An
        /// untagged match takes the sender's id; if we tagged ours too, the lower player's id wins.
        /// </summary>
        private static bool Adopt(DungeonRoom room, SummonSpawned m)
        {
            var at = new Vector2(m.X, m.Y);
            int theirs = (m.Id - SummonIds.Base) / SummonIds.PerPlayer;
            foreach (var e in room.GetComponentsInChildren<Enemy>(false))
            {
                if (e == null || e.enemyStats == null || !e.enemyStats.isAlive || PrefabName(e) != m.Name) continue;
                if (Vector2.Distance(e.transform.position, at) > 48f) continue;
                var tag = e.GetComponent<CoopEnemyId>();
                if (tag == null) { e.gameObject.AddComponent<CoopEnemyId>().Id = m.Id; return true; }
                int roomKey = EnemyOwnership.RoomKey(room.x, room.y, room.isSecret);
                if (!Local.ContainsKey(Key(roomKey, tag.Id))) continue; // an older shared enemy of that kind
                int ours = (tag.Id - SummonIds.Base) / SummonIds.PerPlayer;
                if (theirs < ours) { Local.Remove(Key(roomKey, tag.Id)); tag.Id = m.Id; }
                return true;
            }
            return false;
        }

        private static void SpawnByPrefab(DungeonRoom room, SummonSpawned m)
        {
            var dm = DungeonManager.Instance;
            var prefab = (dm != null ? EnemiesPrefabRegister.GetEnemyByName(dm.culture, m.Name) : null) ?? EnemiesPrefabRegister.GetEnemyByName(m.Name);
            if (prefab == null) { CoopPlugin.Log.LogWarning("Shared summon prefab not found: " + m.Name); return; }
            var go = UnityEngine.Object.Instantiate(prefab, new Vector3(m.X, m.Y, room.transform.position.z), Quaternion.identity, room.transform);
            var e = go.GetComponent<Enemy>();
            if (e == null) { UnityEngine.Object.Destroy(go); return; }
            go.AddComponent<CoopEnemyId>().Id = m.Id;
            e.enemyRoom = room;
            go.SetActive(true);
            e.Init(m.Name);
            if (m.Tile != InstantiateTileNotInRoomList) room.roomEnemies.Add(e);
        }

        private static string PrefabName(Enemy e)
        {
            string n = !string.IsNullOrEmpty(e.name) ? e.name : e.gameObject.name;
            int clone = n.IndexOf("(Clone)", StringComparison.Ordinal);
            return (clone >= 0 ? n.Substring(0, clone) : n).Trim();
        }

        /// <summary>Finds minions made without SpawnEnemy in the current room and shares them.</summary>
        private static void Scan(CoopLink s)
        {
            if (Time.realtimeSinceStartup < _nextScan || FloorSync.ClientBuilding) return;
            _nextScan = Time.realtimeSinceStartup + ScanInterval;
            var room = DungeonManager.Instance.currentDungeonRoom;
            if (room == null) return;
            var all = room.GetComponentsInChildren<Enemy>(false);
            if (room != _scannedRoom)
            {
                _scannedRoom = room;
                Native.Clear();
                foreach (var e in all) if (e != null && e.GetComponent<CoopEnemyId>() == null) Native.Add(e);
                return;
            }
            foreach (var e in all)
            {
                if (e == null || Native.Contains(e) || e.GetComponent<CoopEnemyId>() != null) continue;
                if (e.enemyStats == null || !e.enemyStats.isAlive || e.enemyBehaviour is BreakableObjectBehaviour) { Native.Add(e); continue; }
                if (EnemyMirror.IsFollowed(e)) continue;
                int id = SummonIds.For(s.LocalIndex, _counter++);
                e.gameObject.AddComponent<CoopEnemyId>().Id = id;
                int roomKey = EnemyOwnership.RoomKey(room.x, room.y, room.isSecret);
                Local[Key(roomKey, id)] = e;
                var p = e.transform.position;
                s.SendGame(MessageType.SummonSpawned, new SummonSpawned
                {
                    RoomKey = roomKey, Id = (byte)id, Name = PrefabName(e),
                    Tile = room.roomEnemies.Contains(e) ? InstantiateTile : InstantiateTileNotInRoomList,
                    X = p.x, Y = p.y,
                });
            }
        }

        private static void OnGone(SummonGone m)
        {
            var room = FloorItems.FindRoom(m.RoomKey);
            if (room == null) return;
            foreach (var t in room.GetComponentsInChildren<CoopEnemyId>(true))
            {
                if (t.Id != m.Id) continue;
                var e = t.GetComponent<Enemy>();
                if (e != null) room.roomEnemies.Remove(e);
                UnityEngine.Object.Destroy(t.gameObject);
            }
        }

        /// <summary>Every frame from CoopManager: report our summons that vanished without dying.</summary>
        public static void Tick()
        {
            if (!Sharing(out var s)) return;
            Scan(s);
            if (Local.Count == 0) return;
            List<long> gone = null;
            foreach (var kv in Local)
            {
                var e = kv.Value;
                bool dead = e != null && e.enemyStats != null && !e.enemyStats.isAlive;
                if (e != null && !dead) continue;
                if (e == null) // destroyed while alive: tell the others
                    s.SendGame(MessageType.SummonGone, new SummonGone { RoomKey = (int)(kv.Key >> 8), Id = (byte)(kv.Key & 0xFF) });
                (gone ?? (gone = new List<long>())).Add(kv.Key);
            }
            if (gone != null) foreach (var k in gone) Local.Remove(k);
        }
    }
}
