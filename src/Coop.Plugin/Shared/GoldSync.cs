using System;
using Coop.Core.Protocol;
using Coop.Core.Session;
using Coop.Core.Shared;
using Coop.Plugin.Runtime;
using HarmonyLib;
using UnityEngine;

namespace Coop.Plugin.Shared
{
    /// <summary>
    /// One shared purse. Any change to the local Will's gold is sent as a delta; the host's
    /// total is the truth and is sent on connect and every 2 s for correction.
    /// If both spend at once with too little gold, the host keeps the shortfall as debt and
    /// takes it from the next income (SharedPurse), so no purchase ends up free.
    /// </summary>
    internal static class GoldSync
    {
        public const float TotalInterval = 2f;

        private static bool _applyingRemote;
        private static float _nextTotal;
        private static readonly SharedPurse Purse = new SharedPurse();

        public static void Apply(Harmony harmony)
        {
            harmony.Patch(AccessTools.PropertySetter(typeof(Stat), nameof(Stat.Value)),
                prefix: new HarmonyMethod(typeof(GoldSync), nameof(ValuePrefix)),
                postfix: new HarmonyMethod(typeof(GoldSync), nameof(ValuePostfix)));
            // Loading a save sets gold from the slot: that is not spending or earning.
            harmony.Patch(AccessTools.Method(typeof(HeroMerchantStats), nameof(HeroMerchantStats.Init)),
                prefix: new HarmonyMethod(typeof(GoldSync), nameof(StatsInitPrefix)),
                postfix: new HarmonyMethod(typeof(GoldSync), nameof(StatsInitPostfix)));
        }

        private static bool _loadingSave;
        private static void StatsInitPrefix() => _loadingSave = true;
        private static void StatsInitPostfix() => _loadingSave = false;

        public static void Attach(CoopLink session)
        {
            session.GameMessageReceived += OnGameMessage;
            session.StateChanged += st => { if (st == SessionState.Connected) { _nextTotal = 0f; Purse.Reset(); } };
        }

        private static bool Connected(out CoopLink s)
        {
            s = CoopManager.Instance != null ? CoopManager.Instance.Session : null;
            return s != null && s.State == SessionState.Connected;
        }

        private static Stat LocalGold()
        {
            var hm = HeroMerchant.Instance;
            return hm != null && hm.heroMerchantStats != null ? hm.heroMerchantStats.gold : null;
        }

        private static void ValuePrefix(Stat __instance, out float __state)
        {
            __state = ReferenceEquals(__instance, LocalGold()) ? (float)__instance.Value : float.NaN;
        }

        private static void ValuePostfix(Stat __instance, float __state)
        {
            if (float.IsNaN(__state) || _applyingRemote || _loadingSave || !Connected(out var s) || WorldSync.InTutorial) return;
            int delta = Mathf.RoundToInt((float)__instance.Value - __state);
            if (s.IsHost && delta > 0)
            {
                int pay = Purse.SettleIncome(delta);
                if (pay > 0) { AddLocal(-pay); delta -= pay; }
            }
            if (delta != 0) s.SendGame(MessageType.GoldDelta, new GoldDelta { Delta = delta });
        }

        private static void OnGameMessage(MessageType type, object msg)
        {
            if (WorldSync.InTutorial) return; // the host's total arrives again every 2 s
            if (type == MessageType.GoldDelta)
            {
                int delta = ((GoldDelta)msg).Delta;
                var hm = HeroMerchant.Instance;
                if (hm != null && Connected(out var h) && h.IsHost)
                    SetLocal(Purse.ApplyRemoteDelta(hm.GetCurrentGold(), delta));
                else AddLocal(delta);
            }
            else if (type == MessageType.GoldTotal && Connected(out var s) && !s.IsHost) SetLocal(((GoldTotal)msg).Value);
        }

        private static void AddLocal(int delta)
        {
            var hm = HeroMerchant.Instance;
            if (hm == null || delta == 0) return;
            _applyingRemote = true;
            try { hm.addGold(delta); }
            catch (Exception e) { CoopPlugin.Log.LogWarning("Could not apply shared gold: " + e.Message); }
            finally { _applyingRemote = false; }
        }

        private static void SetLocal(int total)
        {
            var hm = HeroMerchant.Instance;
            if (hm == null) return;
            int diff = total - hm.GetCurrentGold();
            if (diff != 0) AddLocal(diff);
        }

        /// <summary>Host: send the shared total now and then.</summary>
        public static void Tick()
        {
            if (Time.realtimeSinceStartup < _nextTotal) return;
            _nextTotal = Time.realtimeSinceStartup + TotalInterval;
            if (!Connected(out var s) || !s.IsHost || HeroMerchant.Instance == null) return;
            s.SendGame(MessageType.GoldTotal, new GoldTotal { Value = HeroMerchant.Instance.GetCurrentGold() });
        }
    }
}
