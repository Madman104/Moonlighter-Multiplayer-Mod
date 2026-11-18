using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Coop.Plugin.Dungeon
{
    /// <summary>
    /// An enemy attacks only the player whose game runs it.
    ///
    /// On the other games that enemy is a mirror: its AI is paused and it is moved to the
    /// runner's positions, so it chases and swings at the runner. Unity still sends trigger and
    /// collision events to disabled components, so without this guard the mirror's contact and
    /// attack hitboxes also hurt the local Will: the enemy "targets one player but hits both".
    /// When the local player gets closer, ownership moves to their game and the enemy turns on
    /// them for real.
    /// </summary>
    internal static class EnemyHitGuard
    {
        public static void Apply(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(HeroMerchant), nameof(HeroMerchant.DealDamageToPlayer)),
                prefix: new HarmonyMethod(typeof(EnemyHitGuard), nameof(DamagePrefix)));

            // AttackHit also applies on-hit effects (poison, slow) before the damage call.
            var prefix = new HarmonyMethod(typeof(EnemyHitGuard), nameof(AttackHitPrefix));
            int patched = 0;
            foreach (var type in AccessTools.GetTypesFromAssembly(typeof(EnemyBehaviour).Assembly))
            {
                if (type == null || !typeof(EnemyBehaviour).IsAssignableFrom(type)) continue;
                var m = type.GetMethod("AttackHit", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                    null, new[] { typeof(GameObject) }, null);
                if (m == null || m.IsAbstract) continue;
                try { harmony.Patch(m, prefix: prefix); patched++; }
                catch (Exception e) { CoopPlugin.Log.LogWarning($"Could not guard {type.Name}.AttackHit: {e.Message}"); }
            }
            CoopPlugin.Log.LogInfo($"Enemy hit guard on {patched} AttackHit methods");
        }

        private static bool DamagePrefix(Enemy enemySource) => !EnemyMirror.IsFollowed(enemySource);

        private static bool AttackHitPrefix(EnemyBehaviour __instance) => !EnemyMirror.IsFollowed(__instance);
    }
}
