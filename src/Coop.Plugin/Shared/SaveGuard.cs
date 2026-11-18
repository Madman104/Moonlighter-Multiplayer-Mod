using System;
using System.Collections.Generic;
using Coop.Core.Session;
using Coop.Plugin.Runtime;
using FullSerializer;
using HarmonyLib;

namespace Coop.Plugin.Shared
{
    /// <summary>
    /// The shared world lives in the host's save. A guest's own save keeps only what is theirs
    /// from co-op: their bag, equipment and weapon set. Everything else (gold, day, story,
    /// shop, dungeon...) stays as it was when they joined.
    ///
    /// How: when a guest connects, a copy of their save slot is taken. Whenever the game writes
    /// the save (GameManager.SerializeGameSlot, the only writer), that copy - with the current
    /// bag and equipment put in - is written instead of the live co-op slot. If the copy can't be
    /// made, the save isn't written at all (the old, safe behaviour).
    ///
    /// After a guest disconnects, the in-memory game still holds co-op state, so the same rule
    /// applies until a save is loaded again. Returning to the main menu ends the co-op session.
    /// </summary>
    internal static class SaveGuard
    {
        private static bool _guestStateLoaded;
        private static string _ownSlotJson; // the guest's slot as it was when they joined
        private static int _ownSeed;        // which save slot that copy belongs to
        private static bool _loggedBlock;

        public static void Apply(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(GameManager), nameof(GameManager.SerializeGameSlot)),
                prefix: new HarmonyMethod(typeof(SaveGuard), nameof(SerializePrefix)),
                finalizer: new HarmonyMethod(typeof(SaveGuard), nameof(SerializeFinalizer)));
            harmony.Patch(AccessTools.Method(typeof(HeroMerchantStats), nameof(HeroMerchantStats.Init)),
                postfix: new HarmonyMethod(typeof(SaveGuard), nameof(FreshLoadPostfix)));
            harmony.Patch(AccessTools.Method(typeof(GameManager), nameof(GameManager.ReturnToMainMenu)),
                prefix: new HarmonyMethod(typeof(SaveGuard), nameof(MainMenuPrefix)));
        }

        /// <summary>Called when a session connects as guest.</summary>
        public static void MarkGuest()
        {
            _guestStateLoaded = true;
            if (_ownSlotJson != null) return; // still the copy from before an earlier co-op session
            var gm = GameManager.Instance;
            if (gm == null || gm.currentGameSlot == null) return;
            try
            {
                FsJSONManager.Serialize(gm.currentGameSlot, out fsData data);
                _ownSlotJson = data.ToString();
                _ownSeed = gm.currentGameSlot.seed;
                var check = ReadOwnSlot(); // prove the copy can be read back before relying on it
                CoopPlugin.Log.LogInfo(check != null
                    ? "Co-op: as a guest, your save keeps your own bag and equipment; gold, day and story stay as before"
                    : "Co-op: could not copy your save; it won't be written while playing as a guest");
                if (check == null) _ownSlotJson = null;
            }
            catch (Exception e)
            {
                _ownSlotJson = null;
                CoopPlugin.Log.LogWarning("Co-op: could not copy your save (" + e.Message + "); it won't be written while playing as a guest");
            }
        }

        private static GameSlot ReadOwnSlot()
        {
            GameSlot slot = null;
            FsJSONManager.Deserialize(_ownSlotJson, ref slot, false);
            return slot;
        }

        private struct Swap
        {
            public int Index;
            public GameSlot Live;
        }

        private static bool SerializePrefix(GameManager __instance, out Swap __state)
        {
            __state = new Swap { Index = -1 };
            var s = CoopManager.Instance != null ? CoopManager.Instance.Session : null;
            if (s != null && !s.IsHost && s.State == SessionState.Connected) _guestStateLoaded = true;
            if (!_guestStateLoaded) return true;

            var live = __instance.currentGameSlot;
            var slots = __instance.gameSaveData != null ? __instance.gameSaveData.gameSlots : null;
            int index = slots != null ? Array.IndexOf(slots, live) : -1;
            GameSlot own = null;
            if (_ownSlotJson != null && live != null && index >= 0 && live.seed == _ownSeed)
            {
                try { own = ReadOwnSlot(); }
                catch (Exception e) { CoopPlugin.Log.LogWarning("Co-op: could not read your saved copy: " + e.Message); }
            }
            if (own == null)
            {
                if (!_loggedBlock)
                {
                    _loggedBlock = true;
                    CoopPlugin.Log.LogInfo("Co-op: your own save is not written while (or after) playing as a guest; reload your save to play solo");
                }
                return false;
            }

            // The game has already copied the bag and equipment into the live slot (OnSave events).
            own.willInventory = new List<ItemSaveInfo>(live.willInventory);
            own.willEquippedItems = new List<ItemSaveInfo>(live.willEquippedItems);
            own.equippedWeaponSet = live.equippedWeaponSet;
            slots[index] = own;
            __instance.currentGameSlot = own; // SerializeGameSlot stamps the current slot
            __state = new Swap { Index = index, Live = live };
            try
            {
                FsJSONManager.Serialize(own, out fsData data);
                _ownSlotJson = data.ToString(); // later saves start from what was written
            }
            catch (Exception) { /* keep the older copy */ }
            return true;
        }

        // Always put the live co-op slot back, even if writing failed.
        private static Exception SerializeFinalizer(GameManager __instance, Swap __state, Exception __exception)
        {
            if (__state.Index >= 0 && __state.Live != null)
            {
                __instance.gameSaveData.gameSlots[__state.Index] = __state.Live;
                __instance.currentGameSlot = __state.Live;
            }
            return __exception;
        }

        // A save was (re)loaded: the in-memory game is this player's own again.
        private static void FreshLoadPostfix()
        {
            var s = CoopManager.Instance != null ? CoopManager.Instance.Session : null;
            if (s == null || s.State != SessionState.Connected)
            {
                _guestStateLoaded = false;
                _ownSlotJson = null;
                _loggedBlock = false;
                return;
            }
            // A guest loaded a different save while connected: copy that one instead.
            var gm = GameManager.Instance;
            if (!s.IsHost && gm != null && gm.currentGameSlot != null && gm.currentGameSlot.seed != _ownSeed)
            {
                _ownSlotJson = null;
                MarkGuest();
            }
        }

        private static void MainMenuPrefix()
        {
            if (CoopManager.Instance != null && CoopManager.Instance.IsActive)
            {
                CoopPlugin.Log.LogInfo("Co-op: leaving the session (returned to main menu)");
                CoopManager.Instance.Leave();
            }
        }
    }
}
