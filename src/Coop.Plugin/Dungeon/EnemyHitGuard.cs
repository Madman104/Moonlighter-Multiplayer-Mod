using System;
using System.Collections.Generic;
using System.Reflection;
using Coop.Core.Protocol;
using Coop.Core.Session;
using Coop.Plugin.Runtime;
using Coop.Plugin.Sync;
using HarmonyLib;
using UnityEngine;

namespace Coop.Plugin.Dungeon
{
    /// <summary>
    /// Who an enemy's hit lands on.
    ///
    /// Each enemy is run by one game (EnemyMirror). On the other games it's a mirror: its AI is
    /// paused and it is moved to the runner's positions. Unity still sends trigger and collision
    /// events to disabled components, so without this guard a mirror's hitboxes would hurt the
    /// local Will too. So: a mirrored enemy never hurts the local Will.
    ///
    /// Instead the runner hits everyone: other players' figures have a body there
    /// (PartnerHitbox). When an enemy, its contact or one of its projectiles/areas hits a
    /// partner's body, the damage is sent to that player (PartnerHit) and the local Will is
    /// left alone (no damage, push or on-hit effect). The partner's game applies it unless they
    /// are dodging (intangible) or a ghost.
    /// </summary>
    internal static class EnemyHitGuard
    {
        private const float HitCooldown = 0.5f; // per partner, like Will's own hit invulnerability

        private static GameObject _contact;
        private static bool _applyingPartnerHit;
        private static readonly Dictionary<int, float> NextHit = new Dictionary<int, float>();

        public static void Apply(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(HeroMerchant), nameof(HeroMerchant.DealDamageToPlayer)),
                prefix: new HarmonyMethod(typeof(EnemyHitGuard), nameof(DamagePrefix)));
            var push = AccessTools.Method(typeof(HeroMerchantController), nameof(HeroMerchantController.AddForce));
            if (push != null) harmony.Patch(push, prefix: new HarmonyMethod(typeof(EnemyHitGuard), nameof(PushPrefix)));

            var attackPrefix = new HarmonyMethod(typeof(EnemyHitGuard), nameof(AttackHitPrefix));
            var enter = new HarmonyMethod(typeof(EnemyHitGuard), nameof(ContactPrefix));
            var leave = new HarmonyMethod(typeof(EnemyHitGuard), nameof(ContactFinalizer));
            int attacks = 0, contacts = 0;
            foreach (var type in AccessTools.GetTypesFromAssembly(typeof(EnemyBehaviour).Assembly))
            {
                if (type == null || type.IsAbstract && type.IsSealed) continue;
                bool enemy = typeof(EnemyBehaviour).IsAssignableFrom(type);
                if (enemy)
                {
                    var m = type.GetMethod("AttackHit", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                        null, new[] { typeof(GameObject) }, null);
                    if (m != null && !m.IsAbstract)
                    {
                        try { harmony.Patch(m, prefix: attackPrefix); attacks++; }
                        catch (Exception e) { CoopPlugin.Log.LogWarning($"Could not guard {type.Name}.AttackHit: {e.Message}"); }
                    }
                }
                if (!enemy && !IsAttackObject(type)) continue;
                foreach (var name in new[] { "OnTriggerEnter2D", "OnTriggerStay2D", "OnCollisionEnter2D", "OnCollisionStay2D" })
                {
                    var m = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    if (m == null || m.IsAbstract || m.GetParameters().Length != 1) continue;
                    try { harmony.Patch(m, prefix: enter, finalizer: leave); contacts++; }
                    catch (Exception e) { CoopPlugin.Log.LogWarning($"Could not watch {type.Name}.{name}: {e.Message}"); }
                }
            }
            CoopPlugin.Log.LogInfo($"Enemy hit guard on {attacks} AttackHit and {contacts} contact methods");
        }

        public static void Attach(CoopLink session)
        {
            NextHit.Clear();
            session.GameMessageReceived += (type, msg) =>
            {
                if (type == MessageType.PartnerHit) OnPartnerHit(session, (PartnerHit)msg);
            };
        }

        /// <summary>Projectiles, gas/fire areas and traps that enemies leave: they hit on contact.</summary>
        private static bool IsAttackObject(Type t)
        {
            if (!typeof(MonoBehaviour).IsAssignableFrom(t)) return false;
            string n = t.Name;
            if (n.StartsWith("HeroMerchant", StringComparison.Ordinal) || n.Contains("Familiar") || n.StartsWith("TrickWeapon", StringComparison.Ordinal)) return false;
            return n.Contains("Projectile") || n.Contains("Proyectile") || n.EndsWith("AreaEffect", StringComparison.Ordinal)
                || n.Contains("Trap") || n.Contains("Explosion") || n.Contains("Spike");
        }

        // ---- contact context: which object a collision callback is about ----

        private static void ContactPrefix(object[] __args, out GameObject __state)
        {
            __state = _contact;
            var a = __args != null && __args.Length > 0 ? __args[0] : null;
            if (a is Collider2D c) _contact = c != null ? c.gameObject : null;
            else if (a is Collision2D col) _contact = col != null ? col.gameObject : null;
        }

        private static Exception ContactFinalizer(Exception __exception, GameObject __state)
        {
            _contact = __state;
            return __exception;
        }

        // ---- guards ----

        private static bool DamagePrefix(float attackStrength, string damageSource, Enemy enemySource)
        {
            if (_applyingPartnerHit) return true;
            int partner = PartnerHitbox.PlayerOf(_contact);
            if (partner >= 0) { Forward(partner, attackStrength, damageSource); return false; }
            return !EnemyMirror.IsFollowed(enemySource);
        }

        private static bool PushPrefix() => PartnerHitbox.PlayerOf(_contact) < 0;

        private static bool AttackHitPrefix(EnemyBehaviour __instance, object[] __args)
        {
            if (EnemyMirror.IsFollowed(__instance)) return false;
            int partner = PartnerHitbox.PlayerOf(__args != null && __args.Length > 0 ? __args[0] as GameObject : null);
            if (partner < 0) return true;
            var e = __instance != null ? __instance.enemy : null;
            if (e != null && e.enemyStats != null && e.enemyStats.isAlive) Forward(partner, e.enemyStats.strength, e.name);
            return false;
        }

        // ---- sending and receiving ----

        private static void Forward(int partner, float damage, string source)
        {
            var s = CoopManager.Instance != null ? CoopManager.Instance.Session : null;
            if (s == null || s.State != SessionState.Connected || partner == s.LocalIndex) return;
            float now = Time.time;
            if (NextHit.TryGetValue(partner, out float next) && now < next) return;
            NextHit[partner] = now + HitCooldown;
            s.SendGame(MessageType.PartnerHit, new PartnerHit { Target = (byte)partner, Damage = damage, Source = source ?? "" });
        }

        private static void OnPartnerHit(CoopLink s, PartnerHit hit)
        {
            if (hit.Target != s.LocalIndex || !SharedCultures.InSharedDungeon() || Shared.GhostMode.LocalGhost) return;
            var will = HeroMerchant.Instance;
            if (will == null || will.heroMerchantStats == null || will.heroMerchantStats.isDead) return;
            int intangible = LayerMask.NameToLayer("IntangibleHero");
            if (intangible >= 0 && will.gameObject.layer == intangible) return; // dodged on this screen
            _applyingPartnerHit = true;
            try { will.DealDamageToPlayer(hit.Damage, hit.Source, HeroMerchant.DamageType.Normal, null); }
            catch (Exception e) { CoopPlugin.Log.LogWarning("Could not apply a hit from another game: " + e.Message); }
            finally { _applyingPartnerHit = false; }
        }
    }
}
