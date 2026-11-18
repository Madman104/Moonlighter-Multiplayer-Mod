using System;
using System.Collections.Generic;
using Coop.Core.Protocol;
using Coop.Core.Session;
using Coop.Plugin.Runtime;
using FullSerializer;
using HarmonyLib;
using UnityEngine;

namespace Coop.Plugin.Shared
{
    /// <summary>
    /// The guest plays in the host's world: town, shop, upgrades, quests, calendar.
    ///
    /// When a guest connects, the host (once in town) sends its whole save slot. The guest's
    /// game goes to the main menu if needed and loads that slot like Continue would, with the
    /// guest's own bag, equipment and weapon set put in. The guest's own save is never
    /// overwritten with the host's world (SaveGuard writes only their bag and gear).
    /// </summary>
    internal static class WorldSync
    {
        /// <summary>Guest: the host's world is being loaded (not this player's own save).</summary>
        public static bool LoadingHostWorld { get; private set; }

        /// <summary>Guest: returning to the main menu to load the host's world; keep the session.</summary>
        public static bool KeepSessionOnMenu { get; private set; }

        /// <summary>Guest: currently playing in the host's world.</summary>
        public static bool InHostWorld { get; private set; }

        private static bool _sendPending;
        private static bool _toldWaiting;
        private static string _pendingJson;
        private static float _menuSince = -1f;
        private static bool _toldPressKey;

        public static void Attach(PeerSession session)
        {
            _pendingJson = null;
            _sendPending = session.IsHost;
            _toldWaiting = false;
            session.GameMessageReceived += (type, msg) =>
            {
                if (type != MessageType.WorldSlot || session.IsHost) return;
                try { _pendingJson = PackedText.Unpack(((WorldSlot)msg).Data); }
                catch (Exception e) { CoopPlugin.Log.LogError("Could not read the host's world: " + e.Message); return; }
                CoopPlugin.Log.LogInfo($"Received the host's world ({_pendingJson.Length} chars)");
            };
            session.StateChanged += st =>
            {
                if (st == SessionState.Connected && !session.IsHost) CoopToast.Show("Connected - joining the host's world...");
                if (st != SessionState.Closed) return;
                _pendingJson = null;
                KeepSessionOnMenu = false;
                LoadingHostWorld = false;
                if (InHostWorld && !session.IsHost) GuestLostHost();
            };
        }

        private static bool Connected(out PeerSession s)
        {
            s = CoopManager.Instance != null ? CoopManager.Instance.Session : null;
            return s != null && s.State == SessionState.Connected;
        }

        public static void Tick()
        {
            if (!Connected(out var s)) return;
            if (s.IsHost) HostTick(s);
            else GuestTick();
        }

        // ---- host ----

        private static void HostTick(PeerSession s)
        {
            if (!_sendPending) return;
            var gm = GameManager.Instance;
            if (gm == null || HeroMerchant.Instance == null || gm.currentGameSlot == null) return;
            if (!gm.IsTownSceneLoaded() || gm.IsDungeonSceneLoaded())
            {
                if (!_toldWaiting && gm.IsDungeonSceneLoaded())
                {
                    _toldWaiting = true;
                    CoopToast.Show("Your partner joins when you're back in town");
                }
                return;
            }
            _sendPending = false;
            SendWorld(s, gm);
        }

        /// <summary>Host: refresh the slot from the live game (as entering a dungeon does) and send it.</summary>
        private static void SendWorld(PeerSession s, GameManager gm)
        {
            try
            {
                var save = Traverse.Create(gm).Field("OnSaveToDungeon").GetValue() as Delegate;
                save?.DynamicInvoke(gm.currentGameSlot);
                FsJSONManager.Serialize(gm.currentGameSlot, out fsData data);
                string json = data.ToString();
                var packed = PackedText.Pack(json);
                s.SendGame(MessageType.WorldSlot, new WorldSlot { Data = packed });
                CoopPlugin.Log.LogInfo($"Sent the world to the partner ({json.Length} chars, {packed.Length} bytes packed)");
            }
            catch (Exception e)
            {
                CoopPlugin.Log.LogError("Could not send the world: " + e);
            }
        }

        // ---- guest ----

        private static void GuestTick()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;

            if (LoadingHostWorld)
            {
                if (gm.IsTownSceneLoaded() && HeroMerchant.Instance != null && Time.timeSinceLevelLoad > 1f)
                {
                    LoadingHostWorld = false;
                    InHostWorld = true;
                    CoopToast.Show("You're in the host's world");
                    CoopPlugin.Log.LogInfo("Co-op: playing in the host's world");
                }
                return;
            }
            if (_pendingJson == null) return;

            // In a game: go to the main menu first (the game only loads a slot from there).
            if (HeroMerchant.Instance != null && (gm.IsTownSceneLoaded() || gm.IsDungeonSceneLoaded()))
            {
                if (KeepSessionOnMenu) return; // already on the way
                KeepSessionOnMenu = true;
                CoopPlugin.Log.LogInfo("Co-op: going to the main menu to load the host's world");
                gm.ReturnToMainMenu();
                return;
            }

            var menu = UnityEngine.Object.FindObjectOfType<MainMenuPanel>();
            if (menu == null) return;
            if (_menuSince < 0f) _menuSince = Time.realtimeSinceStartup;
            bool open = Traverse.Create(menu).Field("_open").GetValue<bool>();
            if (!open)
            {
                if (!_toldPressKey && Time.realtimeSinceStartup - _menuSince > 3f)
                {
                    _toldPressKey = true;
                    CoopToast.Show("Press any key to join the host's world");
                }
                return;
            }
            if (Time.realtimeSinceStartup - _menuSince < 1f) return; // let the menu settle
            LoadHostWorld(gm, menu);
        }

        private static void LoadHostWorld(GameManager gm, MainMenuPanel menu)
        {
            if (!PrepareWorld(gm)) return;
            menu.StartGame();
        }

        /// <summary>
        /// Puts the host's world (with this player's own bag and gear) in as the slot to load.
        /// </summary>
        private static bool PrepareWorld(GameManager gm)
        {
            string json = _pendingJson;
            _pendingJson = null;
            _menuSince = -1f;
            _toldPressKey = false;
            KeepSessionOnMenu = false;
            GameSlot world = null;
            try { FsJSONManager.Deserialize(json, ref world, false); }
            catch (Exception e) { CoopPlugin.Log.LogError("Could not read the host's world: " + e.Message); }
            if (world == null) { CoopToast.Show("Could not load the host's world"); return false; }

            // Keep this player's own bag, equipment and weapon set.
            var own = SaveGuard.OwnSlotCopy();
            world.willInventory = own != null ? new List<ItemSaveInfo>(own.willInventory) : new List<ItemSaveInfo>();
            world.willEquippedItems = own != null ? new List<ItemSaveInfo>(own.willEquippedItems) : new List<ItemSaveInfo>();
            world.equippedWeaponSet = own != null ? own.equippedWeaponSet : 0;
            if (own != null) { world.equippedFamiliar = own.equippedFamiliar; world.familiarsDiscovered = own.familiarsDiscovered; }

            LoadingHostWorld = true;
            gm.currentGameSlot = world;
            CoopPlugin.Log.LogInfo("Co-op: loading the host's world");
            return true;
        }

        public static void Apply(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(MainMenuPanel), nameof(MainMenuPanel.StartGame)),
                prefix: new HarmonyMethod(typeof(WorldSync), nameof(StartGamePrefix)));
        }

        // The guest pressed Continue (or our own call): load the host's world if it has arrived.
        private static void StartGamePrefix()
        {
            if (_pendingJson == null || !Connected(out var s) || s.IsHost) return;
            var gm = GameManager.Instance;
            if (gm != null) PrepareWorld(gm);
        }

        // The host went away while the guest was in the host's world: back to the guest's own game.
        private static void GuestLostHost()
        {
            InHostWorld = false;
            var gm = GameManager.Instance;
            if (gm == null || HeroMerchant.Instance == null) return;
            CoopToast.Show("Lost the host. Your bag and gear are kept; back to the main menu");
            CoopPlugin.Log.LogInfo("Co-op: host gone; saving your bag and returning to the main menu");
            try
            {
                // Writes this player's own slot with the current bag and gear (SaveGuard).
                var save = Traverse.Create(gm).Field("OnSaveToDungeon").GetValue() as Delegate;
                save?.DynamicInvoke(gm.currentGameSlot);
                gm.SerializeGameSlot();
            }
            catch (Exception e) { CoopPlugin.Log.LogWarning("Could not save your bag: " + e.Message); }
            try { gm.ReturnToMainMenu(); }
            catch (Exception e) { CoopPlugin.Log.LogWarning("Could not return to the main menu: " + e.Message); }
        }
    }
}
