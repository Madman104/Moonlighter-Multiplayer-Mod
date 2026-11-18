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
    /// </summary>
    internal static class SummonSync
    {
        private static readonly Dictionary<long, Enemy> Local = new Dictionary<long, Enemy>();
        private static int _counter;
        private static bool _remoteSpawn;
        private static int _remoteId;

        public static void Attach(CoopLink session)
        {
            Local.Clear();
            _counter = 0;
            session.GameMessageReceived += (type, msg) =>
            {
                if (type == MessageType.SummonSpawned)
                {
                    var m = (SummonSpawned)msg;
                    int scene = GameInfo.CurrentSceneId();
                    LateApply.Run("summon " + m.Name, () => GameInfo.CurrentSceneId() != scene || TrySpawn(m));
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
            if (room == null) return false;
            foreach (var t in room.GetComponentsInChildren<CoopEnemyId>(true))
                if (t.Id == m.Id && t.GetComponent<Enemy>() != null && t.GetComponent<Enemy>().enemyStats != null
                    && t.GetComponent<Enemy>().enemyStats.isAlive) return true; // already have it
            _remoteSpawn = true;
            _remoteId = m.Id;
            try
            {
                var spawn = new DungeonRoomInfo.EnemySpawn { index = m.Tile, enemy = new PrefabSpawnInfo { name = m.Name } };
                var e = room.SpawnEnemy(spawn, m.Tile);
                if (e != null) e.transform.position = new Vector3(m.X, m.Y, e.transform.position.z);
                else CoopPlugin.Log.LogWarning("Could not spawn shared summon " + m.Name);
            }
            catch (Exception e) { CoopPlugin.Log.LogWarning("Could not spawn shared summon " + m.Name + ": " + e.Message); }
            finally { _remoteSpawn = false; }
            return true;
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
            if (Local.Count == 0 || !Sharing(out var s)) return;
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
