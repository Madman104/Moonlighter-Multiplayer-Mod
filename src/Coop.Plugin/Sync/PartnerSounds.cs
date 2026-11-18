using System;
using System.Collections.Generic;
using System.Reflection;
using Coop.Core.Protocol;
using Coop.Core.Session;
using Coop.Core.Sync;
using Coop.Plugin.Runtime;
using DarkTonic.MasterAudio;
using HarmonyLib;
using UnityEngine;

namespace Coop.Plugin.Sync
{
    /// <summary>
    /// Players hear each other's Will: footsteps, swings, charge-ups, getting hurt.
    ///
    /// Sending: Will's sounds come from methods on components under Will (animation events on
    /// SoundsWill / SoundsShortSword / ..., and HeroMerchantController.PlayStepSound etc.).
    /// Every few seconds the components under the local Will are scanned; each of their
    /// methods whose IL calls MasterAudio is patched to mark "inside Will's code" while it
    /// runs, but only when the instance really is under the local Will. A MasterAudio play or
    /// stop made inside that mark is sent (throttled) as PlayerSound.
    ///
    /// Receiving: played at a volume that falls off with the distance to that player's Will,
    /// scaled to the camera view; nothing when they're in another place.
    /// </summary>
    internal static class PartnerSounds
    {
        private const float ScanInterval = 3f;

        private sealed class Partner
        {
            public Vector2 Pos;
            public int Scene;
            public float At;
            public readonly Dictionary<string, List<SoundGroupVariation>> Playing = new Dictionary<string, List<SoundGroupVariation>>();
        }

        private static Harmony _harmony;
        private static readonly HashSet<Type> ScannedTypes = new HashSet<Type>();
        private static readonly HashSet<MethodBase> Patched = new HashSet<MethodBase>();
        private static readonly Dictionary<int, Partner> Partners = new Dictionary<int, Partner>();
        private static readonly SoundThrottle Throttle = new SoundThrottle();
        private static int _willDepth;
        private static bool _replaying;
        private static float _nextScan;

        public static void Apply(Harmony harmony)
        {
            _harmony = harmony;
            var playPrefix = new HarmonyMethod(typeof(PartnerSounds), nameof(PlayPrefix));
            int n = 0;
            foreach (var m in typeof(MasterAudio).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (!m.Name.StartsWith("PlaySound", StringComparison.Ordinal)) continue;
                var ps = m.GetParameters();
                if (ps.Length < 2 || ps[0].Name != "sType" || ps[0].ParameterType != typeof(string)) continue;
                if (Array.FindIndex(ps, p => p.Name == "volumePercentage" && p.ParameterType == typeof(float)) < 0) continue;
                try { harmony.Patch(m, prefix: playPrefix); n++; }
                catch (Exception e) { CoopPlugin.Log.LogWarning($"Could not patch MasterAudio.{m.Name}: {e.Message}"); }
            }
            var stop = AccessTools.Method(typeof(MasterAudio), nameof(MasterAudio.StopAllOfSound), new[] { typeof(string) });
            if (stop != null) harmony.Patch(stop, prefix: new HarmonyMethod(typeof(PartnerSounds), nameof(StopPrefix)));
            CoopPlugin.Log.LogInfo($"Partner sounds: {n} MasterAudio play methods patched");
        }

        public static void Attach(CoopLink session)
        {
            Partners.Clear();
            Throttle.Reset();
            session.PlayerStateReceived += st =>
            {
                var p = Get(session.LastFrom);
                p.Pos = new Vector2(st.X, st.Y);
                p.Scene = st.SceneId;
                p.At = Time.realtimeSinceStartup;
            };
            session.PeerLeft += i => { if (Partners.TryGetValue(i, out var p)) StopAll(p); Partners.Remove(i); };
            session.GameMessageReceived += (type, msg) =>
            {
                if (type == MessageType.PlayerSound) OnSound(session.LastFrom, (PlayerSound)msg);
            };
            session.StateChanged += st =>
            {
                if (st != SessionState.Closed) return;
                foreach (var p in Partners.Values) StopAll(p);
                Partners.Clear();
            };
        }

        private static Partner Get(int player)
        {
            if (!Partners.TryGetValue(player, out var p)) Partners[player] = p = new Partner();
            return p;
        }

        private static bool Connected(out CoopLink s)
        {
            s = CoopManager.Instance != null ? CoopManager.Instance.Session : null;
            return s != null && s.State == SessionState.Connected;
        }

        // ---- finding Will's sound methods ----

        /// <summary>Every frame from CoopManager: patches sound methods of new components under Will.</summary>
        public static void Tick()
        {
            if (_harmony == null || Time.realtimeSinceStartup < _nextScan) return;
            _nextScan = Time.realtimeSinceStartup + ScanInterval;
            if (!Connected(out _)) return;
            var will = HeroMerchant.Instance;
            if (will == null) return;
            foreach (var c in will.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (c == null) continue;
                var type = c.GetType();
                if (!ScannedTypes.Add(type)) continue;
                if (type.Assembly == typeof(PartnerSounds).Assembly) continue;
                PatchSoundMethods(type);
            }
        }

        private static void PatchSoundMethods(Type type)
        {
            var enter = new HarmonyMethod(typeof(PartnerSounds), nameof(EnterPrefix));
            var leave = new HarmonyMethod(typeof(PartnerSounds), nameof(LeaveFinalizer));
            int n = 0;
            for (var t = type; t != null && t != typeof(MonoBehaviour); t = t.BaseType)
            {
                foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (m.IsAbstract || m.IsGenericMethodDefinition || m.ContainsGenericParameters || Patched.Contains(m)) continue;
                    if (!CallsMasterAudio(m)) continue;
                    try { _harmony.Patch(m, prefix: enter, finalizer: leave); Patched.Add(m); n++; }
                    catch (Exception e) { CoopPlugin.Log.LogWarning($"Could not patch {t.Name}.{m.Name} for sounds: {e.Message}"); }
                }
            }
            if (n > 0) CoopPlugin.Log.LogInfo($"Partner sounds: {n} sound methods on {type.Name}");
        }

        private static bool CallsMasterAudio(MethodInfo m)
        {
            try
            {
                foreach (var ins in PatchProcessor.ReadMethodBody(m))
                    if (ins.Value is MethodInfo called && called.DeclaringType == typeof(MasterAudio)
                        && (called.Name.StartsWith("PlaySound", StringComparison.Ordinal) || called.Name == nameof(MasterAudio.StopAllOfSound)))
                        return true;
            }
            catch { /* no body (extern) or unreadable */ }
            return false;
        }

        private static void EnterPrefix(Component __instance, out bool __state)
        {
            var will = HeroMerchant.Instance;
            __state = will != null && __instance != null && __instance.transform.IsChildOf(will.transform);
            if (__state) _willDepth++;
        }

        private static Exception LeaveFinalizer(Exception __exception, bool __state)
        {
            if (__state && _willDepth > 0) _willDepth--;
            return __exception;
        }

        // ---- sending ----

        private static void PlayPrefix(string sType, float volumePercentage) => Send(sType, volumePercentage, false);

        private static void StopPrefix(string sType) => Send(sType, 1f, true);

        private static void Send(string name, float volume, bool stop)
        {
            if (_willDepth <= 0 || _replaying || !Connected(out var s)) return;
            if (!Throttle.Allow(name, Time.realtimeSinceStartup, stop)) return;
            s.SendGame(MessageType.PlayerSound, new PlayerSound { Name = name, Volume = volume, Stop = stop });
        }

        // ---- receiving ----

        private static void OnSound(int from, PlayerSound msg)
        {
            if (string.IsNullOrEmpty(msg.Name)) return;
            var p = Get(from);
            if (msg.Stop) { Stop(p, msg.Name); return; }
            float now = Time.realtimeSinceStartup;
            var will = HeroMerchant.Instance;
            var cam = Camera.main;
            if (will == null || now - p.At > 1f || p.Scene != GameInfo.CurrentSceneId() || p.Scene < 0) return;
            float view = cam != null && cam.orthographic ? cam.orthographicSize : 0f;
            if (view <= 0f) return;
            float dist = Vector2.Distance(will.transform.position, p.Pos);
            float range = Mathf.Max(1.5f, CoopConfig.PartnerSoundRange.Value);
            float vol = SoundFalloff.Volume(dist, view, view * range) * Mathf.Clamp01(msg.Volume) * Mathf.Clamp01(CoopConfig.PartnerSoundVolume.Value);
            if (vol <= 0.01f) return;
            _replaying = true;
            try
            {
                var result = MasterAudio.PlaySound(msg.Name, vol);
                if (result != null && result.ActingVariation != null)
                {
                    if (!p.Playing.TryGetValue(msg.Name, out var list)) p.Playing[msg.Name] = list = new List<SoundGroupVariation>();
                    list.RemoveAll(v => v == null || !v.IsPlaying);
                    list.Add(result.ActingVariation);
                }
            }
            catch (Exception e) { CoopPlugin.Log.LogWarning($"Could not play partner sound {msg.Name}: {e.Message}"); }
            finally { _replaying = false; }
        }

        /// <summary>Stops only what this partner started, not the local Will's own copy.</summary>
        private static void Stop(Partner p, string name)
        {
            if (!p.Playing.TryGetValue(name, out var list)) return;
            foreach (var v in list)
                if (v != null && v.IsPlaying)
                {
                    try { v.Stop(); } catch (Exception e) { CoopPlugin.Log.LogWarning("Could not stop partner sound: " + e.Message); }
                }
            list.Clear();
        }

        private static void StopAll(Partner p)
        {
            foreach (var name in new List<string>(p.Playing.Keys)) Stop(p, name);
        }
    }
}
