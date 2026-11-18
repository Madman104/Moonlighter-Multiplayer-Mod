using System;
using System.Collections.Generic;
using Coop.Core.Dungeon;
using Coop.Core.Protocol;
using Coop.Core.Session;
using Coop.Plugin.Runtime;
using HarmonyLib;
using UnityEngine;

namespace Coop.Plugin.Dungeon
{
    /// <summary>
    /// Items on dungeon floors are shared: one copy for everyone, and whoever picks it up first
    /// gets it.
    /// - Enemy loot is rolled only by the game that runs the enemy (deaths happen on every game;
    ///   the others skip the loot roll). Quest-target drops stay per player (quests are per player).
    /// - Any other item that lands on a dungeon floor in someone's game (loot rolled by the host,
    ///   a broken pot, boss loot from the AI runner, an item dropped from a full bag) is sent
    ///   once it has landed; everyone else puts the same item at the same place.
    /// - When it leaves the floor in a game (picked up, fell, destroyed) the others remove it;
    ///   a partial pickup sends what's left.
    /// - A ghost's dropped bag stays local (it's that player's own items).
    /// Two players touching the same item at once: the host decides (first claim it hears
    /// wins) and tells the other one, whose game takes the item back out of their bag.
    /// A player who joins late gets every item still lying on the floor (ResendTo).
    /// </summary>
    internal static class FloorItems
    {
        private sealed class Entry
        {
            public ItemStack Stack;
            public Transform Parent; // where the game put it on the floor
            public DungeonRoom Room;
            public int Qty;
            public bool Sent;
        }

        /// <summary>Set by the mod around drops that must stay local (a ghost's own bag).</summary>
        public static bool LocalOnly;

        private static readonly Dictionary<int, Entry> Items = new Dictionary<int, Entry>();
        private static readonly Dictionary<ItemStack, int> IdOf = new Dictionary<ItemStack, int>();
        /// <summary>Host: who got each shared item (first claim wins).</summary>
        private static readonly Dictionary<int, int> Claimed = new Dictionary<int, int>();

        private sealed class Taken { public ItemMaster Master; public int Qty; public float At; }

        /// <summary>Items this game just picked up, in case the host says someone else got them.</summary>
        private static readonly Dictionary<int, Taken> Recent = new Dictionary<int, Taken>();
        public const int Revoked = -1;
        private static int _enemyDeath, _lootRoll, _counter, _scene = int.MinValue;
        private static bool _remoteCreate;

        public static void Apply(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(Enemy), nameof(Enemy.DropItemsOnDeath)),
                prefix: new HarmonyMethod(typeof(FloorItems), nameof(DeathPrefix)),
                finalizer: new HarmonyMethod(typeof(FloorItems), nameof(DeathFinalizer)));
            harmony.Patch(AccessTools.Method(typeof(ItemDrop), nameof(ItemDrop.DropItem)),
                prefix: new HarmonyMethod(typeof(FloorItems), nameof(LootPrefix)),
                finalizer: new HarmonyMethod(typeof(FloorItems), nameof(LootFinalizer)));
            var dropPostfix = new HarmonyMethod(typeof(FloorItems), nameof(DropPostfix));
            foreach (var m in typeof(ItemStack).GetMethods())
                if (m.Name == nameof(ItemStack.Drop) && m.DeclaringType == typeof(ItemStack))
                    harmony.Patch(m, postfix: dropPostfix);
        }

        public static void Attach(CoopLink session)
        {
            Clear();
            _counter = 0;
            session.GameMessageReceived += (type, msg) =>
            {
                if (type == MessageType.FloorItemSpawn) OnSpawn((FloorItemSpawn)msg);
                else if (type == MessageType.FloorItemTaken) OnTaken(session, session.LastFrom, (FloorItemTaken)msg);
            };
            session.StateChanged += st => { if (st == SessionState.Closed) Clear(); };
        }

        private static void Clear()
        {
            Items.Clear();
            IdOf.Clear();
            Claimed.Clear();
            Recent.Clear();
        }

        private static bool Connected(out CoopLink s)
        {
            s = CoopManager.Instance != null ? CoopManager.Instance.Session : null;
            return s != null && s.State == SessionState.Connected;
        }

        private static bool Sharing(out CoopLink s) => Connected(out s) && SharedCultures.InSharedDungeon();

        // ---- enemy loot only from the host ----

        private static bool _skipLoot;

        private static void DeathPrefix(Enemy __instance)
        {
            _enemyDeath++;
            _skipLoot = EnemySync.NoLoot.Contains(__instance) || !EnemyMirror.RunsHere(__instance);
        }
        private static Exception DeathFinalizer(Exception __exception) { if (_enemyDeath > 0) _enemyDeath--; return __exception; }

        private static bool LootPrefix(ref List<ItemStack> __result, out bool __state)
        {
            __state = false;
            if (_enemyDeath > 0 && _skipLoot && Sharing(out _))
            {
                __result = new List<ItemStack>(); // the game running this enemy rolls its loot
                return false;
            }
            __state = true;
            _lootRoll++;
            return true;
        }

        private static Exception LootFinalizer(Exception __exception, bool __state)
        {
            if (__state && _lootRoll > 0) _lootRoll--;
            return __exception;
        }

        // ---- local drops become shared ----

        private static void DropPostfix(ItemStack __instance)
        {
            if (__instance == null || _remoteCreate || LocalOnly || !Sharing(out var s)) return;
            if (_enemyDeath > 0 && _lootRoll == 0) return; // quest-target drop: per player
            if (IdOf.ContainsKey(__instance)) return;
            var room = __instance.GetComponentInParent<DungeonRoom>();
            if (room == null) return;
            int id = (s.LocalIndex << 24) | (++_counter & 0xFFFFFF);
            Track(id, __instance, room, false);
        }

        private static void Track(int id, ItemStack stack, DungeonRoom room, bool sent)
        {
            Items[id] = new Entry { Stack = stack, Parent = stack.transform.parent, Room = room, Qty = stack.Quantity, Sent = sent };
            IdOf[stack] = id;
        }

        private static void Forget(int id, Entry e)
        {
            Items.Remove(id);
            if (e.Stack != null) IdOf.Remove(e.Stack);
            else
            {
                var dead = new List<ItemStack>();
                foreach (var kv in IdOf) if (kv.Value == id) dead.Add(kv.Key);
                foreach (var k in dead) IdOf.Remove(k);
            }
        }

        private static bool InBag(ItemStack stack)
        {
            var hm = HeroMerchant.Instance;
            if (hm == null || hm.heroMerchantInventory == null) return false;
            var inv = hm.heroMerchantInventory;
            return inv.HasItemInBag(stack.gameObject) || inv.HasItemEquipped(stack.gameObject);
        }

        private static bool OnFloor(Entry e) => e.Stack != null && e.Parent != null && e.Stack.transform.parent == e.Parent && !InBag(e.Stack);

        /// <summary>Every frame from CoopManager: send landed items, report items that left the floor.</summary>
        public static void Tick()
        {
            if (!Connected(out var s)) return;
            int scene = GameInfo.CurrentSceneId();
            if (scene != _scene) { _scene = scene; Clear(); return; } // floor changed: old items are gone everywhere
            if (Items.Count == 0) return;
            List<int> gone = null;
            foreach (var kv in Items)
            {
                var e = kv.Value;
                if (!OnFloor(e))
                {
                    if (e.Sent)
                    {
                        if (e.Stack != null && InBag(e.Stack) && e.Stack.master != null)
                            Recent[kv.Key] = new Taken { Master = e.Stack.master, Qty = e.Qty, At = Time.realtimeSinceStartup };
                        if (s.IsHost && Claimed.TryGetValue(kv.Key, out int first) && first != s.LocalIndex)
                            RevokeLocal(kv.Key); // a guest's claim reached us first
                        else
                        {
                            if (s.IsHost) Claimed[kv.Key] = s.LocalIndex;
                            s.SendGame(MessageType.FloorItemTaken, new FloorItemTaken { Id = kv.Key, Remaining = 0 });
                        }
                    }
                    (gone ?? (gone = new List<int>())).Add(kv.Key);
                    continue;
                }
                if (!e.Sent)
                {
                    var pick = e.Stack.GetComponentInChildren<PickOnWalk>(true);
                    if (pick == null || !pick.CanPick) continue; // still flying
                    if (e.Room == null) continue;
                    s.SendGame(MessageType.FloorItemSpawn, SpawnOf(kv.Key, e));
                    e.Sent = true;
                    e.Qty = e.Stack.Quantity;
                }
                else if (e.Stack.Quantity != e.Qty)
                {
                    e.Qty = e.Stack.Quantity;
                    s.SendGame(MessageType.FloorItemTaken, new FloorItemTaken { Id = kv.Key, Remaining = e.Qty });
                }
            }
            if (gone != null) foreach (var id in gone) Forget(id, Items[id]);
            if (Recent.Count > 0)
            {
                float now = Time.realtimeSinceStartup;
                var old = new List<int>();
                foreach (var kv in Recent) if (now - kv.Value.At > 30f) old.Add(kv.Key);
                foreach (var id in old) Recent.Remove(id);
            }
        }

        private static FloorItemSpawn SpawnOf(int id, Entry e)
        {
            var p = e.Stack.transform.position;
            return new FloorItemSpawn
            {
                Id = id,
                RoomKey = EnemyOwnership.RoomKey(e.Room.x, e.Room.y, e.Room.isSecret),
                X = p.x,
                Y = p.y,
                Json = ToJson(new ItemSaveInfo(e.Stack)),
            };
        }

        /// <summary>Host: a guest joined late; send every shared item still on this floor.</summary>
        public static void ResendTo(CoopLink s, int guest)
        {
            int n = 0;
            foreach (var kv in Items)
                if (kv.Value.Sent && OnFloor(kv.Value) && kv.Value.Room != null)
                {
                    s.SendGameTo(guest, MessageType.FloorItemSpawn, SpawnOf(kv.Key, kv.Value));
                    n++;
                }
            if (n > 0) CoopPlugin.Log.LogInfo($"Sent {n} floor item(s) to the late player");
        }

        /// <summary>The host gave an item we picked up to someone else: take it back out of the bag.</summary>
        private static void RevokeLocal(int id)
        {
            if (!Recent.TryGetValue(id, out var t)) return;
            Recent.Remove(id);
            var hm = HeroMerchant.Instance;
            if (hm == null || hm.heroMerchantInventory == null) return;
            try
            {
                int removed = hm.heroMerchantInventory.RemoveItem(t.Master, t.Qty);
                CoopPlugin.Log.LogInfo($"Shared item {t.Master.name} x{t.Qty} went to another player (removed {removed})");
                CoopToast.Show("Your partner grabbed that first");
            }
            catch (Exception e) { CoopPlugin.Log.LogWarning("Could not give back a shared item: " + e.Message); }
        }

        // ---- receiving ----

        private static void OnSpawn(FloorItemSpawn msg)
        {
            int scene = GameInfo.CurrentSceneId();
            LateApply.Run("floor item " + msg.Id, () => GameInfo.CurrentSceneId() != scene || TrySpawn(msg));
        }

        private static bool TrySpawn(FloorItemSpawn msg)
        {
            if (Items.ContainsKey(msg.Id) || !Sharing(out _)) return true;
            var room = FindRoom(msg.RoomKey);
            if (room == null) return false;
            ItemStack stack = null;
            _remoteCreate = true;
            try
            {
                ItemSaveInfo info = null;
                FsJSONManager.Deserialize(msg.Json, ref info, false);
                stack = Shared.ItemFactory.Build(info);
                if (stack == null) { CoopPlugin.Log.LogWarning("Shared floor item not known here: " + msg.Json); return true; }
                stack.Drop(new Vector2(msg.X, msg.Y), 0f, room.transform);
                room.AddDropItems(new List<ItemStack> { stack });
                Track(msg.Id, stack, room, true);
            }
            catch (Exception e)
            {
                CoopPlugin.Log.LogWarning("Could not place a shared floor item: " + e.Message);
                if (stack != null) Shared.ItemFactory.Destroy(stack);
            }
            finally { _remoteCreate = false; }
            return true;
        }

        private static void OnTaken(CoopLink s, int from, FloorItemTaken msg)
        {
            if (msg.Remaining == Revoked) { RevokeLocal(msg.Id); return; }
            if (s.IsHost && msg.Remaining == 0)
            {
                if (Claimed.TryGetValue(msg.Id, out int first) && first != from)
                {
                    s.SendGameTo(from, MessageType.FloorItemTaken, new FloorItemTaken { Id = msg.Id, Remaining = Revoked });
                    return;
                }
                Claimed[msg.Id] = from;
            }
            if (!Items.TryGetValue(msg.Id, out var e)) return;
            if (!OnFloor(e)) { Forget(msg.Id, e); return; } // we took it too (same moment): keep ours
            if (msg.Remaining > 0)
            {
                e.Stack.Quantity = msg.Remaining;
                e.Qty = msg.Remaining;
                return;
            }
            Forget(msg.Id, e);
            Shared.ItemFactory.Destroy(e.Stack);
        }

        private static string ToJson(ItemSaveInfo info)
        {
            FsJSONManager.Serialize(info, out FullSerializer.fsData data);
            return data.ToString();
        }

        internal static DungeonRoom FindRoom(int key)
        {
            var dm = DungeonManager.Instance;
            if (dm == null) return null;
            foreach (var r in dm.dungeonRooms)
                if (r != null && EnemyOwnership.RoomKey(r.x, r.y, r.isSecret) == key) return r;
            var boss = dm.bossRoom;
            if (boss != null && EnemyOwnership.RoomKey(boss.x, boss.y, boss.isSecret) == key) return boss;
            return null;
        }
    }
}
