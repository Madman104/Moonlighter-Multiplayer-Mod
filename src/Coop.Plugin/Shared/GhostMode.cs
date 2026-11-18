using System;
using Coop.Core.Dungeon;
using Coop.Core.Protocol;
using Coop.Core.Session;
using Coop.Plugin.Runtime;
using HarmonyLib;
using UnityEngine;

namespace Coop.Plugin.Shared
{
    /// <summary>
    /// Dying in a shared dungeon while someone else is alive: you spectate. Your Will is hidden,
    /// can't move, be hurt, hurt enemies or touch anything, and your view follows a living
    /// player (lowest player number) from room to room and floor to floor. Nothing is dropped.
    /// The run ends for you as a normal death - the game's own results screen and item loss,
    /// exactly like solo - when the watched players go home, or when nobody is left alive.
    /// (The class keeps its old name; "ghost" in messages means "out of play".)
    /// </summary>
    internal sealed class GhostMode : MonoBehaviour
    {
        public const float GhostAlpha = 0.4f;

        public static bool LocalGhost { get; private set; }
        private sealed class Other { public float Hp = -1f; public bool Ghost; public float At; public Vector2 Pos; public int Room = PlayerState.NoRoom; public int Scene = -1; }
        private static readonly System.Collections.Generic.Dictionary<int, Other> Others = new System.Collections.Generic.Dictionary<int, Other>();

        private static bool Fresh(Other o) => Time.realtimeSinceStartup - o.At < 5f;

        /// <summary>Another player who can still act: alive, or still loading (health unknown).</summary>
        private static bool AnyOtherAlive()
        {
            foreach (var o in Others.Values) if (Fresh(o) && !o.Ghost && (o.Hp > 0f || o.Hp < 0f)) return true;
            return false;
        }

        private static bool AnyOtherGhost()
        {
            foreach (var o in Others.Values) if (Fresh(o) && o.Ghost) return true;
            return false;
        }

        private static bool _realDeath;

        public static void Apply(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(HeroMerchantStats), nameof(HeroMerchantStats.Die)),
                prefix: new HarmonyMethod(typeof(GhostMode), nameof(DiePrefix)));
            harmony.Patch(AccessTools.Method(typeof(HeroMerchant), nameof(HeroMerchant.DealDamageToPlayer)),
                prefix: new HarmonyMethod(typeof(GhostMode), nameof(BlockIfGhost)));
            harmony.Patch(AccessTools.Method(typeof(Enemy), nameof(Enemy.DealDamageToEnemy)),
                prefix: new HarmonyMethod(typeof(GhostMode), nameof(BlockIfGhost)));
            harmony.Patch(AccessTools.Method(typeof(HealingArea), "ApplyHealingEffect"),
                prefix: new HarmonyMethod(typeof(GhostMode), nameof(PoolPrefix)));
            harmony.Patch(AccessTools.Method(typeof(GameManager), nameof(GameManager.LoadTownSceneFromDungeon)),
                prefix: new HarmonyMethod(typeof(GhostMode), nameof(TownPrefix)));
            // Ghosts can't drink potions.
            harmony.Patch(AccessTools.Method(typeof(HeroMerchantController), nameof(HeroMerchantController.ConsumeHotkeyItem)),
                prefix: new HarmonyMethod(typeof(GhostMode), nameof(BlockIfGhost)));
            // Ghosts can't pick up or attract items (the attract check asks CanPlaceItem).
            harmony.Patch(AccessTools.Method(typeof(PickOnWalk), nameof(PickOnWalk.TryToAdd)),
                prefix: new HarmonyMethod(typeof(GhostMode), nameof(BlockIfGhost)));
            harmony.Patch(AccessTools.Method(typeof(HeroMerchantInventory), nameof(HeroMerchantInventory.CanPlaceItem), new[] { typeof(ItemStack) }),
                prefix: new HarmonyMethod(typeof(GhostMode), nameof(CanPlacePrefix)));
        }

        public static void Attach(CoopLink session)
        {
            Others.Clear();
            session.PlayerStateReceived += s =>
            {
                int from = session.LastFrom;
                if (!Others.TryGetValue(from, out var o)) Others[from] = o = new Other();
                o.Hp = s.Hp;
                o.Ghost = s.Ghost;
                o.At = Time.realtimeSinceStartup;
                o.Pos = new Vector2(s.X, s.Y);
                o.Room = s.RoomKey;
                o.Scene = s.SceneId;
            };
            session.PeerLeft += i =>
            {
                Others.Remove(i);
                // Host: the last other player left while you're a ghost: nobody can revive you.
                if (session.IsHost && LocalGhost && !AnyOtherAlive()) RealDeath("nobody is left to revive you");
            };
            session.GameMessageReceived += (type, msg) =>
            {
                if (type == MessageType.PoolSpent) { DrainPoolIn((PoolSpent)msg); return; }
                if (type == MessageType.Revive && LocalGhost)
                {
                    var hm = HeroMerchant.Instance;
                    float max = hm != null ? (float)hm.heroMerchantStats.currentHealth.maxValue : 100f;
                    Exit(GhostRules.ReviveHealth(max));
                    CoopPlugin.Log.LogInfo("Co-op: you were revived");
                }
            };
            session.StateChanged += st =>
            {
                // Partner gone while you're a ghost: nobody can revive you. The host dies for real;
                // a guest goes back to the main menu with their bag (WorldSync), so just stop being a ghost.
                if (st == SessionState.Closed && LocalGhost)
                {
                    if (session.IsHost) RealDeath("partner left while you were a ghost");
                    else Exit(1f);
                }
            };
        }

        private static bool Connected(out CoopLink s)
        {
            s = CoopManager.Instance != null ? CoopManager.Instance.Session : null;
            return s != null && s.State == SessionState.Connected;
        }

        private static bool InSharedDungeon()
        {
            // Only where enemies are shared; elsewhere a ghost could never be revived fairly.
            return Dungeon.SharedCultures.InSharedDungeon();
        }

        private static bool DiePrefix()
        {
            if (_realDeath || LocalGhost) return !LocalGhost || _realDeath;
            if (!GhostRules.ShouldBecomeGhost(Connected(out _), InSharedDungeon(), AnyOtherAlive() ? 1f : 0f, false)) return true;
            Enter();
            return false;
        }

        private static bool BlockIfGhost() => !LocalGhost;

        private static bool CanPlacePrefix(ref bool __result)
        {
            if (!LocalGhost) return true;
            __result = false;
            return false;
        }

        private static bool _dropPending;
        private static readonly System.Collections.Generic.List<ItemStack> Pile = new System.Collections.Generic.List<ItemStack>();

        /// <summary>
        /// The session ended before the dropped bag was picked up: put what is still lying on
        /// the floor back into the bag, so leaving co-op doesn't lose it. (A real death or going
        /// home leaves the pile behind, as the game does.)
        /// </summary>
        public static void ReclaimPile()
        {
            var hm = HeroMerchant.Instance;
            if (hm == null) { Pile.Clear(); return; }
            var inv = hm.heroMerchantInventory;
            int back = 0;
            foreach (var item in Pile)
            {
                if (item == null || inv.HasItemInBag(item.gameObject) || inv.HasItemEquipped(item.gameObject)) continue;
                try { if (inv.TryAddItem(item)) back++; }
                catch (Exception e) { CoopPlugin.Log.LogWarning("Could not put a dropped item back: " + e.Message); }
            }
            Pile.Clear();
            if (back > 0) CoopPlugin.Log.LogInfo($"Co-op: put {back} dropped item(s) back in your bag");
        }

        /// <summary>
        /// Drops every bag item that isn't in a safe slot at Will's feet, the way the game drops
        /// items (ItemStack.Drop + the room's drop list, so the pile stays in that room).
        /// Equipment and potions stay equipped.
        /// </summary>
        private static void DropBag()
        {
            var hm = HeroMerchant.Instance;
            if (hm == null) return;
            var inv = hm.heroMerchantInventory;
            var room = DungeonManager.Instance != null ? DungeonManager.Instance.currentDungeonRoom : null;
            Transform parent = room != null ? room.transform : null;
            Vector2 at = hm.transform.position;
            int dropped = 0;
            for (int i = 0; i < inv.slots.Length; i++)
            {
                var slot = inv.slots[i];
                if (slot == null || slot.item == null || slot.isSafe) continue;
                var item = slot.item;
                inv.SetItem(null, i);
                Pile.Add(item);
                if (room != null) room.AddDropItems(new System.Collections.Generic.List<ItemStack> { item });
                Dungeon.FloorItems.LocalOnly = true; // a ghost's own bag isn't shared loot
                try { item.Drop(at, 30f, parent); }
                finally { Dungeon.FloorItems.LocalOnly = false; }
                dropped++;
            }
            if (dropped > 0) CoopToast.Show("Your items dropped where you fell - pick them up once you're revived");
            CoopPlugin.Log.LogInfo($"Co-op: dropped {dropped} bag item(s) on becoming a ghost");
        }

        private static bool PoolPrefix(HealingArea __instance)
        {
            if (LocalGhost) return false; // spectators can't use pools
            return true; // spectating players aren't revived: pools just heal
#pragma warning disable CS0162

            var t = Traverse.Create(__instance);
            float pool = t.Field("healingHealth").GetValue<float>();
            if (!GhostRules.CanRevive(LocalGhost, AnyOtherGhost(), pool) || !Connected(out var s)) return true;

            s.SendGame(MessageType.Revive, null);
            var room = t.Field("myRoom").GetValue<DungeonRoom>();
            if (room != null)
                s.SendGame(MessageType.PoolSpent, new PoolSpent { X = (short)room.x, Y = (short)room.y, Secret = room.isSecret });
            Drain(__instance);
            CoopPlugin.Log.LogInfo("Co-op: healing pool used to revive the ghosts");
            return false;
#pragma warning restore CS0162
        }

        /// <summary>Empties a healing pool and darkens it, as the game does when it runs dry.</summary>
        private static void Drain(HealingArea pool)
        {
            var t = Traverse.Create(pool);
            t.Field("healingHealth").SetValue(0f);
            float dark = t.Field("darknessTint").GetValue<float>() / 255f;
            var c = new Color(dark, dark, dark, 1f);
            var render = t.Field("render").GetValue<SpriteRenderer>();
            var extra = t.Field("renderExtraLayer").GetValue<SpriteRenderer>();
            if (render != null) render.color = c;
            if (extra != null) extra.color = c;
        }

        private static void DrainPoolIn(PoolSpent p)
        {
            var dm = DungeonManager.Instance;
            if (dm == null) return;
            foreach (var room in dm.dungeonRooms)
            {
                if (room == null || room.x != p.X || room.y != p.Y || room.isSecret != p.Secret) continue;
                foreach (var pool in room.GetComponentsInChildren<HealingArea>(true)) Drain(pool);
                return;
            }
        }

        private static void TownPrefix()
        {
            if (!LocalGhost) return;
            var hm = HeroMerchant.Instance;
            Exit(hm != null ? (float)hm.heroMerchantStats.currentHealth.maxValue : 100f);
            // Going home as a ghost counts as dying: the results screen applies the death penalty.
            if (hm != null) hm.heroMerchantStats.isDead = true;
        }

        private static float _bothGhostSince = -1f;

        private static void Enter()
        {
            LocalGhost = true;
            _spectateStarted = false; // set up in LateUpdate, outside the health setter's callback
            // Health is raised to 1 in LateUpdate: we're inside the health setter's callback here,
            // and the setter would overwrite a value set now.
            CoopPlugin.Log.LogInfo("Co-op: you died; spectating until your partner goes home or everyone is down");
            CoopToast.Show("You died. Spectating your partner - your bag is lost as usual when the run ends");
        }

        // ---- spectating ----

        private static bool _spectateStarted;
        private static int _askedRoom = PlayerState.NoRoom;
        private static float _askedAt;
        private static readonly System.Collections.Generic.List<Collider2D> Disabled = new System.Collections.Generic.List<Collider2D>();

        /// <summary>The living player to watch (lowest player number), or null.</summary>
        private static Other Target()
        {
            int scene = GameInfo.CurrentSceneId();
            Other best = null;
            int bestKey = int.MaxValue;
            foreach (var kv in Others)
            {
                var o = kv.Value;
                if (!Fresh(o) || o.Ghost || o.Hp == 0f || o.Scene != scene) continue;
                if (kv.Key < bestKey) { bestKey = kv.Key; best = o; }
            }
            return best;
        }

        /// <summary>Will is hidden, can't move or touch anything, and is carried along with the watched player.</summary>
        private static void Spectate(HeroMerchant hm)
        {
            var ctrl = hm.heroMerchantController;
            if (!_spectateStarted)
            {
                _spectateStarted = true;
                Disabled.Clear();
                foreach (var c in hm.GetComponentsInChildren<Collider2D>(true))
                    if (c != null && c.enabled) { c.enabled = false; Disabled.Add(c); }
            }
            try { ctrl.DisableMoveInput(); ctrl.ForbidMovement(); } catch (Exception) { }
            var t = Target();
            if (t == null) return;
            var dm = DungeonManager.Instance;
            if (dm != null && t.Room != PlayerState.NoRoom && !Dungeon.FloorSync.ClientBuilding)
            {
                var cur = dm.currentDungeonRoom;
                bool there = cur != null && Coop.Core.Dungeon.EnemyOwnership.RoomKey(cur.x, cur.y, cur.isSecret) == t.Room;
                // Changing room takes a moment (fade): ask once per room, again after 3 s if it didn't happen.
                if (!there && (t.Room != _askedRoom || Time.realtimeSinceStartup - _askedAt > 3f))
                {
                    _askedRoom = t.Room;
                    _askedAt = Time.realtimeSinceStartup;
                    var room = Dungeon.FloorItems.FindRoom(t.Room);
                    if (room != null)
                    {
                        try { dm.SpecialChangeRoomTo(room, false); }
                        catch (Exception e) { CoopPlugin.Log.LogWarning("Could not follow the watched player's room: " + e.Message); }
                    }
                }
            }
            var pos = new Vector3(t.Pos.x, t.Pos.y, hm.transform.position.z);
            hm.transform.position = pos;
            var rb = hm.GetComponent<Rigidbody2D>();
            if (rb != null) { rb.position = t.Pos; rb.velocity = Vector2.zero; }
        }

        private static void StopSpectating(HeroMerchant hm)
        {
            _askedRoom = PlayerState.NoRoom;
            if (!_spectateStarted) return;
            _spectateStarted = false;
            foreach (var c in Disabled) if (c != null) c.enabled = true;
            Disabled.Clear();
            if (hm == null) return;
            try { hm.heroMerchantController.AllowMovement(); hm.heroMerchantController.EnableMoveInput(); } catch (Exception) { }
        }

        private static void RealDeath(string why)
        {
            CoopPlugin.Log.LogInfo("Co-op: " + why);
            Exit(1f);
            var hm = HeroMerchant.Instance;
            if (hm == null) return;
            _realDeath = true;
            try { hm.heroMerchantStats.Die(); }
            finally { _realDeath = false; }
        }

        private static void Exit(float health)
        {
            LocalGhost = false;
            _dropPending = false;
            var hm = HeroMerchant.Instance;
            StopSpectating(hm);
            if (hm == null) return;
            hm.heroMerchantStats.currentHealth.Value = Mathf.Max(1f, health);
            SetAlpha(hm, 1f);
        }

        private static void SetAlpha(HeroMerchant hm, float a)
        {
            foreach (var r in hm.GetComponentsInChildren<SpriteRenderer>(true))
            {
                var c = r.color;
                if (Math.Abs(c.a - a) > 0.001f) { c.a = a; r.color = c; }
            }
        }

        /// <summary>Health and ghost flag for the outgoing PlayerState.</summary>
        public static void Describe(HeroMerchant hm, out float hp, out float maxHp, out bool ghost)
        {
            ghost = LocalGhost;
            maxHp = (float)hm.heroMerchantStats.currentHealth.maxValue;
            hp = LocalGhost ? 0f : (float)hm.heroMerchantStats.currentHealth.Value;
        }

        private void LateUpdate()
        {
            var hm = HeroMerchant.Instance;
            if (!LocalGhost || hm == null) { _bothGhostSince = -1f; return; }
            SetAlpha(hm, 0f); // spectating: Will is not shown
            _dropPending = false; // the bag isn't dropped: it's lost like a solo death when the run ends
            try { Spectate(hm); }
            catch (Exception e) { CoopPlugin.Log.LogWarning("Spectating failed: " + e.Message); }
            if ((float)hm.heroMerchantStats.currentHealth.Value <= 0f)
                hm.heroMerchantStats.currentHealth.Value = 1f; // keep the controller alive

            // Nobody else is alive (all ghosts or dead): nobody can revive -> normal death after 2 s.
            if (!AnyOtherAlive())
            {
                if (_bothGhostSince < 0f) _bothGhostSince = Time.realtimeSinceStartup;
                else if (Time.realtimeSinceStartup - _bothGhostSince > 2f) { _bothGhostSince = -1f; RealDeath("everyone is down"); }
            }
            else _bothGhostSince = -1f;
        }
    }
}
