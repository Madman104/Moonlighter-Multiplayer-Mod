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
                // Partner gone while you're a ghost: nobody can revive you, so it's a real death.
                if (st == SessionState.Closed && LocalGhost)
                {
                    Exit(1f);
                    var hm = HeroMerchant.Instance;
                    if (hm != null) { _realDeath = true; try { hm.heroMerchantStats.Die(); } finally { _realDeath = false; } }
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
            var gm = GameManager.Instance;
            return gm != null && gm.IsDungeonSceneLoaded() && DungeonManager.Instance != null;
        }

        private static bool DiePrefix()
        {
            if (_realDeath || LocalGhost) return !LocalGhost || _realDeath;
            if (!GhostRules.ShouldBecomeGhost(Connected(out _), InSharedDungeon(), PartnerHp, PartnerGhost)) return true;
            Enter();
            return false;
        }

        private static bool BlockIfGhost() => !LocalGhost;

        private static bool PoolPrefix(HealingArea __instance)
        {
            if (LocalGhost) return false; // ghosts can't use pools
            var t = Traverse.Create(__instance);
            float pool = t.Field("healingHealth").GetValue<float>();
            if (!GhostRules.CanRevive(LocalGhost, PartnerGhost, pool) || !Connected(out var s)) return true;

            s.SendGame(MessageType.Revive, null);
            t.Field("healingHealth").SetValue(0f);
            float dark = t.Field("darknessTint").GetValue<float>() / 255f;
            var c = new Color(dark, dark, dark, 1f);
            var render = t.Field("render").GetValue<SpriteRenderer>();
            var extra = t.Field("renderExtraLayer").GetValue<SpriteRenderer>();
            if (render != null) render.color = c;
            if (extra != null) extra.color = c;
            PartnerGhost = false;
            CoopPlugin.Log.LogInfo("Co-op: healing pool used to revive your partner");
            return false;
        }

        private static void TownPrefix()
        {
            if (!LocalGhost) return;
            var hm = HeroMerchant.Instance;
            Exit(hm != null ? (float)hm.heroMerchantStats.currentHealth.maxValue : 100f);
        }

        private static void Enter()
        {
            LocalGhost = true;
            var hm = HeroMerchant.Instance;
            if (hm != null) hm.heroMerchantStats.currentHealth.Value = 1f; // keep the controller alive
            CoopPlugin.Log.LogInfo("Co-op: you are a ghost. Your partner can revive you at a healing pool.");
        }

        private static void Exit(float health)
        {
            LocalGhost = false;
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
            if (LocalGhost && HeroMerchant.Instance != null) SetAlpha(HeroMerchant.Instance, GhostAlpha);
        }

        // Partner's health bar (display only) while in a dungeon together.
        private void OnGUI()
        {
            if (!Connected(out var s) || !InSharedDungeon() || PartnerHp < 0f || PartnerMaxHp <= 0f) return;
            if (_label == null)
            {
                _label = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
                _label.normal.textColor = new Color(0.85f, 0.95f, 0.8f);
            }
            const float x = 24f, y = 110f, w = 220f, h = 16f;
            string who = s.IsHost ? "P2" : "P1";
            GUI.Label(new Rect(x, y - 22f, 260f, 22f), PartnerGhost ? who + " (ghost - find a healing pool)" : who, _label);
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
            float frac = Mathf.Clamp01(PartnerHp / PartnerMaxHp);
            GUI.color = PartnerGhost ? new Color(0.6f, 0.6f, 0.8f, 0.8f) : new Color(0.85f, 0.2f, 0.2f, 1f);
            GUI.DrawTexture(new Rect(x + 2f, y + 2f, (w - 4f) * frac, h - 4f), Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
