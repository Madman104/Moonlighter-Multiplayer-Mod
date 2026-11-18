using System;
using System.Collections.Generic;
using UnityEngine;

namespace Coop.Plugin.Dungeon
{
    /// <summary>
    /// Room messages that arrive before this game has built that room (a late joiner still
    /// building the floor, or a floor change in flight) are kept and retried for a while.
    /// </summary>
    internal static class LateApply
    {
        public const float KeepFor = 20f;

        private sealed class Job { public float Until; public Func<bool> Try; public string What; }

        private static readonly List<Job> Jobs = new List<Job>();

        /// <summary>Runs tryApply now; if it returns false (room not here yet), retries it later.</summary>
        public static void Run(string what, Func<bool> tryApply)
        {
            if (Attempt(tryApply)) return;
            Jobs.Add(new Job { Until = Time.realtimeSinceStartup + KeepFor, Try = tryApply, What = what });
        }

        public static void Clear() => Jobs.Clear();

        /// <summary>Every frame from CoopManager.</summary>
        public static void Tick()
        {
            if (Jobs.Count == 0 || FloorSync.ClientBuilding) return;
            float now = Time.realtimeSinceStartup;
            for (int i = Jobs.Count - 1; i >= 0; i--)
            {
                var j = Jobs[i];
                if (Attempt(j.Try)) Jobs.RemoveAt(i);
                else if (now > j.Until)
                {
                    CoopPlugin.Log.LogInfo("Dropped a late room update: " + j.What);
                    Jobs.RemoveAt(i);
                }
            }
        }

        private static bool Attempt(Func<bool> f)
        {
            if (FloorSync.ClientBuilding) return false;
            try { return f(); }
            catch (Exception e) { CoopPlugin.Log.LogWarning("Room update failed: " + e.Message); return true; }
        }
    }
}
