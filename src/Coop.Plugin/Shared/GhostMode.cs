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
    /// Stage 5: dying in a shared dungeon while the partner is alive makes you a ghost
    /// (see-through, can't be hurt, can't hurt enemies, can't use pools). The living partner
    /// revives you by using a healing pool, which is then used up. Both down = normal death.
    /// Becoming a ghost drops your bag (except safe slots) where you fell, in that room; a
    /// ghost can't pick items up, so walk back to the pile after being revived.
    /// Also draws the partner's health bar in dungeons.
    /// </summary>
    internal sealed class GhostMode : MonoBehaviour
    {
        public const float GhostAlpha = 0.4f;

        public static bool LocalGhost { get; private set; }
        public static float PartnerHp { get; private set; } = -1f;
        public static float PartnerMaxHp { get; private set; }
        public static bool PartnerGhost { get; private set; }

        private static bool _realDeath;
        private GUIStyle _label;
        private float _labelScale;

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

        public static void Attach(PeerSession session)
        {
            PartnerHp = -1f;
            PartnerGhost = false;
            session.PlayerStateReceived += s =>
            {
                PartnerHp = s.Hp;
                PartnerMaxHp = s.MaxHp;
                PartnerGhost = s.Ghost;
            };
            session.GameMessageReceived += (type, msg) =>
            {
                if (type == MessageType.PoolSpent) { DrainPoolIn((PoolSpent)msg); return; }
                if (type == MessageType.Revive && LocalGhost)
                {
                    var hm = HeroMerchant.Instance;
                    float max = hm != null ? (float)hm.heroMerchantStats.currentHealth.maxValue : 100f;
                    Exit(GhostRules.ReviveHealth(max));
                    CoopPlugin.Log.LogInfo("Co-op: your partner revived you");
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

        private static bool Connected(out PeerSession s)
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
            if (!GhostRules.ShouldBecomeGhost(Connected(out _), InSharedDungeon(), PartnerHp, PartnerGhost)) return true;
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
                item.Drop(at, 30f, parent);
                dropped++;
            }
            if (dropped > 0) CoopToast.Show("Your items dropped where you fell - pick them up once you're revived");
            CoopPlugin.Log.LogInfo($"Co-op: dropped {dropped} bag item(s) on becoming a ghost");
        }

        private static bool PoolPrefix(HealingArea __instance)
        {
            if (LocalGhost) return false; // ghosts can't use pools
            var t = Traverse.Create(__instance);
            float pool = t.Field("healingHealth").GetValue<float>();
            if (!GhostRules.CanRevive(LocalGhost, PartnerGhost, pool) || !Connected(out var s)) return true;

            s.SendGame(MessageType.Revive, null);
            var room = t.Field("myRoom").GetValue<DungeonRoom>();
            if (room != null)
                s.SendGame(MessageType.PoolSpent, new PoolSpent { X = (short)room.x, Y = (short)room.y, Secret = room.isSecret });
            Drain(__instance);
            PartnerGhost = false;
            CoopPlugin.Log.LogInfo("Co-op: healing pool used to revive your partner");
            return false;
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
            _dropPending = true; // done in LateUpdate, outside the health setter's callback
            // Health is raised to 1 in LateUpdate: we're inside the health setter's callback here,
            // and the setter would overwrite a value set now.
            CoopPlugin.Log.LogInfo("Co-op: you are a ghost. Your partner can revive you at a healing pool.");
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
            SetAlpha(hm, GhostAlpha);
            if (_dropPending)
            {
                _dropPending = false;
                try { DropBag(); }
                catch (Exception e) { CoopPlugin.Log.LogWarning("Could not drop the bag: " + e.Message); }
            }
            if ((float)hm.heroMerchantStats.currentHealth.Value <= 0f)
                hm.heroMerchantStats.currentHealth.Value = 1f; // keep the controller alive

            // Partner really died (not a ghost, no health): both are down -> normal death.
            if (PartnerHp == 0f && !PartnerGhost) { RealDeath("both players are down"); return; }

            // Both became ghosts at the same moment: nobody can revive -> normal death after 2 s.
            if (PartnerGhost)
            {
                if (_bothGhostSince < 0f) _bothGhostSince = Time.realtimeSinceStartup;
                else if (Time.realtimeSinceStartup - _bothGhostSince > 2f) { _bothGhostSince = -1f; RealDeath("both players are ghosts"); }
            }
            else _bothGhostSince = -1f;
        }

        // Partner's health bar (display only) while in a dungeon together.
        private void OnGUI()
        {
            if (!Connected(out var s) || !InSharedDungeon() || PartnerHp < 0f || PartnerMaxHp <= 0f) return;
            // Below the game's own health bar, bag icon and gold (top-left), scaled with the screen.
            float k = Screen.height / 1080f;
            if (_label == null || _labelScale != k)
            {
                _labelScale = k;
                _label = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(20 * k), fontStyle = FontStyle.Bold };
                _label.normal.textColor = new Color(0.95f, 0.95f, 0.9f);
            }
            float x = 28f * k, y = 236f * k, w = 200f * k, h = 18f * k;
            string who = PlayerNames.Partner;
            string text = PartnerGhost ? who + " - ghost: use a healing pool" : who + "  " + Mathf.CeilToInt(PartnerHp) + "/" + Mathf.CeilToInt(PartnerMaxHp);
            GUI.Label(new Rect(x, y - 28f * k, 600f * k, 28f * k), text, _label);
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
            float frac = Mathf.Clamp01(PartnerHp / PartnerMaxHp);
            GUI.color = PartnerGhost ? new Color(0.6f, 0.6f, 0.8f, 0.8f) : new Color(0.94f, 0.33f, 0.29f, 1f);
            GUI.DrawTexture(new Rect(x + 2f * k, y + 2f * k, (w - 4f * k) * frac, h - 4f * k), Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
