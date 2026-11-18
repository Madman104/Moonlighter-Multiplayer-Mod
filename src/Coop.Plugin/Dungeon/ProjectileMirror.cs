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
    /// Enemy projectiles exist only in the game that runs the enemy. That game sends where its
    /// projectiles are and what they look like 10 times a second; the other games draw sprite
    /// copies. Hits are decided by the runner (EnemyHitGuard + PartnerHitbox).
    /// Projectiles register themselves when they wake up (Awake/Start/OnEnable patched), so
    /// nothing searches the scene each tick; the few types with none of those are looked up
    /// once a second.
    /// </summary>
    internal static class ProjectileMirror
    {
        private const float Interval = 0.1f;
        private const float Stale = 0.35f;

        private sealed class Seen { public EntityState State; public float At; public int Key; }

        private static readonly List<Type> Types = new List<Type>();
        private static readonly List<Type> Unhooked = new List<Type>();
        private static readonly HashSet<MonoBehaviour> Live = new HashSet<MonoBehaviour>();
        private static float _nextSlowScan;

        public static void Apply(HarmonyLib.Harmony harmony)
        {
            FindTypes();
            var post = new HarmonyLib.HarmonyMethod(typeof(ProjectileMirror), nameof(WokePostfix));
            int hooked = 0;
            foreach (var t in Types)
            {
                bool any = false;
                foreach (var name in new[] { "Awake", "Start", "OnEnable" })
                {
                    var m = t.GetMethod(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                    if (m == null) continue;
                    try { harmony.Patch(m, postfix: post); any = true; }
                    catch (Exception e) { CoopPlugin.Log.LogWarning($"Could not watch {t.Name}.{name}: {e.Message}"); }
                }
                if (any) hooked++; else Unhooked.Add(t);
            }
            CoopPlugin.Log.LogInfo($"Projectile mirror: {hooked} types register themselves, {Unhooked.Count} looked up");
        }

        private static void WokePostfix(MonoBehaviour __instance) { if (__instance != null) Live.Add(__instance); }

        private static void FindTypes()
        {
            if (Types.Count > 0) return;
            foreach (var t in HarmonyLib.AccessTools.GetTypesFromAssembly(typeof(EnemyBehaviour).Assembly))
                if (t != null && typeof(MonoBehaviour).IsAssignableFrom(t) && !t.IsAbstract
                    && (t.Name.Contains("Projectile") || t.Name.Contains("Proyectile"))
                    && !t.Name.StartsWith("HeroMerchant", StringComparison.Ordinal) && !t.Name.Contains("Familiar")
                    && !t.Name.StartsWith("TrickWeapon", StringComparison.Ordinal) && t != typeof(ProjectilePool))
                    Types.Add(t);
        }
        private static readonly Dictionary<int, ushort> IdOf = new Dictionary<int, ushort>();
        private static ushort _nextId = 1;
        private static readonly Dictionary<long, Seen> Incoming = new Dictionary<long, Seen>();
        private static readonly Dictionary<long, GameObject> Drawn = new Dictionary<long, GameObject>();
        private static float _next;

        public static void Attach(CoopLink session)
        {
            FindTypes();
            Clear();
            session.GameMessageReceived += (type, msg) =>
            {
                if (type != MessageType.EntityStates) return;
                var es = (EntityStates)msg;
                if (es.Group != EntityStates.Projectiles) return;
                OnStates(session.LastFrom, es);
            };
            session.StateChanged += st => { if (st == SessionState.Closed) Clear(); };
        }

        private static void Clear()
        {
            Incoming.Clear();
            foreach (var go in Drawn.Values) if (go != null) UnityEngine.Object.Destroy(go);
            Drawn.Clear();
            IdOf.Clear();
        }

        private static long Key(int from, ushort id) => ((long)from << 16) | id;

        private static void OnStates(int from, EntityStates es)
        {
            float now = Time.realtimeSinceStartup;
            var seen = new HashSet<long>();
            foreach (var e in es.Entities)
            {
                long k = Key(from, e.Id);
                seen.Add(k);
                Incoming[k] = new Seen { State = e, At = now, Key = es.Key };
            }
            var gone = new List<long>();
            foreach (var kv in Incoming) if ((kv.Key >> 16) == from && !seen.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var k in gone) Incoming.Remove(k);
        }

        /// <summary>Every frame from CoopManager.</summary>
        public static void Tick()
        {
            var s = CoopManager.Instance != null ? CoopManager.Instance.Session : null;
            if (s == null || s.State != SessionState.Connected) return;
            var room = SharedCultures.InSharedDungeon() && !FloorSync.ClientBuilding ? DungeonManager.Instance.currentDungeonRoom : null;
            if (room == null) { if (Drawn.Count > 0 || Incoming.Count > 0) Clear(); return; }
            int key = EnemyOwnership.RoomKey(room.x, room.y, room.isSecret);
            float now = Time.realtimeSinceStartup;
            if (now >= _next)
            {
                _next = now + Interval;
                Send(s, key);
            }
            Draw(key, now);
        }

        private static void Send(CoopLink s, int key)
        {
            var list = new List<EntityState>();
            if (Unhooked.Count > 0 && Time.realtimeSinceStartup >= _nextSlowScan)
            {
                _nextSlowScan = Time.realtimeSinceStartup + 1f;
                foreach (var t in Unhooked)
                {
                    try { foreach (var o in UnityEngine.Object.FindObjectsOfType(t)) if (o is MonoBehaviour mb) Live.Add(mb); }
                    catch { }
                }
            }
            Live.RemoveWhere(c => c == null);
            {
                foreach (var c in Live)
                {
                    if (!c.isActiveAndEnabled || list.Count >= 60) continue;
                    int iid = c.gameObject.GetInstanceID();
                    if (!IdOf.TryGetValue(iid, out var id))
                    {
                        id = _nextId++;
                        if (_nextId == 0) _nextId = 1;
                        IdOf[iid] = id;
                    }
                    list.Add(EnemyMirror.Describe(s, c.transform, id));
                }
            }
            if (IdOf.Count > 500) IdOf.Clear();
            if (list.Count == 0 && _sentEmpty) return; // nothing flying: one empty message is enough
            _sentEmpty = list.Count == 0;
            s.SendFrequent(MessageType.EntityStates, new EntityStates { Group = EntityStates.Projectiles, Key = key, Entities = list.ToArray() });
        }

        private static bool _sentEmpty;

        private static void Draw(int key, float now)
        {
            var keep = new HashSet<long>();
            foreach (var kv in Incoming)
            {
                if (kv.Value.Key != key || now - kv.Value.At > Stale) continue;
                keep.Add(kv.Key);
                var target = new Vector3(kv.Value.State.X, kv.Value.State.Y, 0f);
                if (!Drawn.TryGetValue(kv.Key, out var go) || go == null)
                {
                    go = new GameObject("CoopProjectileCopy");
                    go.transform.position = target;
                    Drawn[kv.Key] = go;
                }
                // Projectiles are fast: follow closely, no long smoothing.
                go.transform.position = Vector3.Lerp(go.transform.position, target, Mathf.Clamp01(Time.deltaTime * 30f));
                EnemyMirror.DrawLayers(go, kv.Value.State, (int)(kv.Key >> 16));
            }
            if (Drawn.Count == keep.Count) return;
            var gone = new List<long>();
            foreach (var k in Drawn.Keys) if (!keep.Contains(k)) gone.Add(k);
            foreach (var k in gone)
            {
                if (Drawn[k] != null) UnityEngine.Object.Destroy(Drawn[k]);
                Drawn.Remove(k);
            }
        }
    }
}
