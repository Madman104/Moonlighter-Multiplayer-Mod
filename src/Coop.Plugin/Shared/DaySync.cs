using System;
using Coop.Core.Protocol;
using Coop.Core.Session;
using Coop.Plugin.Runtime;
using HarmonyLib;
using UnityEngine;

namespace Coop.Plugin.Shared
{
    /// <summary>
    /// Stage 6: the host's time of day is the truth. When the host sleeps, the day ends for
    /// both; the guest's own bed only shows a notice.
    /// </summary>
    internal static class DaySync
    {
        public const float SyncInterval = 5f;

        private static bool _applyingRemote;
        private static float _nextSync;

        public static void Apply(Harmony harmony)
        {
            var post = new HarmonyMethod(typeof(DaySync), nameof(DayChangedPostfix));
            harmony.Patch(AccessTools.Method(typeof(TimeManagerV2), nameof(TimeManagerV2.SwapDayTime)), postfix: post);
            harmony.Patch(AccessTools.Method(typeof(TimeManagerV2), nameof(TimeManagerV2.ChangeToMorning)), postfix: post);
            harmony.Patch(AccessTools.Method(typeof(TimeManagerV2), nameof(TimeManagerV2.ChangeToAfternoon)), postfix: post);
            harmony.Patch(AccessTools.Method(typeof(Bed), nameof(Bed.SleepInBed)),
                prefix: new HarmonyMethod(typeof(DaySync), nameof(SleepPrefix)));
        }

        public static void Attach(CoopLink session)
        {
            session.GameMessageReceived += OnGameMessage;
            session.StateChanged += st => { if (st == SessionState.Connected) _nextSync = 0f; };
        }

        private static bool Connected(out CoopLink s)
        {
            s = CoopManager.Instance != null ? CoopManager.Instance.Session : null;
            return s != null && s.State == SessionState.Connected;
        }

        // A day change within this many seconds of the host lying down came from the bed.
        private const float BedWindow = 30f;
        private static float _hostSleptAt = -100f;

        private static bool TakeFromBed()
        {
            bool fromBed = Time.realtimeSinceStartup - _hostSleptAt < BedWindow;
            _hostSleptAt = -100f;
            return fromBed;
        }

        private static int TotalDays() => MoonlighterCalendar.Instance != null ? MoonlighterCalendar.TotalDays : -1;

        private static bool SleepPrefix()
        {
            if (!Connected(out var s)) return true;
            if (s.IsHost) { _hostSleptAt = Time.realtimeSinceStartup; return true; }
            CoopToast.Show("Only the host's bed ends the day");
            return false;
        }

        private static void DayChangedPostfix()
        {
            if (_applyingRemote || !Connected(out var s) || !s.IsHost || TimeManagerV2.Instance == null) return;
            s.SendGame(MessageType.DayTimeSync, new DayTimeSync
            {
                Value = (byte)TimeManagerV2.Instance.CurrrentDayTime,
                FromBed = TakeFromBed(), // closing the shop also turns the day to evening
                TotalDays = TotalDays(),
            });
        }

        private static void OnGameMessage(MessageType type, object msg)
        {
            if (type != MessageType.DayTimeSync || !Connected(out var s) || s.IsHost || WorldSync.InTutorial) return;
            var tm = TimeManagerV2.Instance;
            if (tm == null) return;
            var d = (DayTimeSync)msg;
            var target = (DayTime)d.Value;
            FollowCalendar(d.TotalDays);
            if (tm.CurrrentDayTime == target) return;
            _applyingRemote = true;
            try
            {
                if (target == DayTime.Afternoon) tm.ChangeToAfternoon();
                else tm.ChangeToMorning();
                if (GUIManager.Instance != null && GUIManager.Instance.dayAfternoonPanel != null)
                    GUIManager.Instance.dayAfternoonPanel.SetAngle(tm.CurrrentDayTime);
            }
            catch (Exception e)
            {
                CoopPlugin.Log.LogWarning("Could not follow the host's time of day: " + e.Message);
            }
            finally
            {
                _applyingRemote = false;
            }
            FollowCalendar(d.TotalDays); // the change above may have added a day
            if (d.FromBed)
            {
                CoopToast.Show(target == DayTime.Afternoon ? "Your partner rested: it's now evening" : "Your partner slept: a new day begins");
                // Everyone wakes up rested: the bed's bonus shield/absorption, as if they'd slept too.
                try
                {
                    var slot = GameManager.Instance != null ? GameManager.Instance.currentGameSlot : null;
                    if (HeroMerchant.Instance != null && slot != null && slot.townboardSaveInfo != null)
                        ShopManager.GiveWillBedBenefits(slot.townboardSaveInfo.GetBedLevel());
                }
                catch (Exception e) { CoopPlugin.Log.LogWarning("Could not give the bed bonus: " + e.Message); }
            }
        }

        // The guest's day counter follows the host's (only morning/evening was synced before).
        private static void FollowCalendar(int hostDays)
        {
            if (hostDays < 0 || MoonlighterCalendar.Instance == null || MoonlighterCalendar.TotalDays == hostDays) return;
            try { MoonlighterCalendar.Instance.Init(hostDays); }
            catch (Exception e) { CoopPlugin.Log.LogWarning("Could not follow the host's calendar: " + e.Message); }
        }

        /// <summary>Host: repeat the time of day now and then (late joiners, drift).</summary>
        public static void Tick()
        {
            if (Time.realtimeSinceStartup < _nextSync) return;
            _nextSync = Time.realtimeSinceStartup + SyncInterval;
            if (!Connected(out var s) || !s.IsHost || TimeManagerV2.Instance == null) return;
            s.SendGame(MessageType.DayTimeSync, new DayTimeSync { Value = (byte)TimeManagerV2.Instance.CurrrentDayTime, FromBed = false, TotalDays = TotalDays() });
        }
    }
}
