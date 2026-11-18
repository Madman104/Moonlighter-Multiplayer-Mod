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

        private static readonly System.Collections.Generic.HashSet<int> SendTo = new System.Collections.Generic.HashSet<int>();
        private static bool _toldWaiting;
        private static string _pendingJson;
        private static float _menuSince = -1f;
        private static bool _toldPressKey;
        private static float _menuRequestedAt;

        public static void Attach(CoopLink session)
        {
            _pendingJson = null;
            SendTo.Clear();
            session.PeerJoined += i => { if (session.IsHost) { SendTo.Add(i); _toldWaiting = false; } };
            session.PeerLeft += i => SendTo.Remove(i);
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

        private static bool Connected(out CoopLink s)
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

        private static void HostTick(CoopLink s)
        {
            if (SendTo.Count == 0) return;
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
            var targets = new System.Collections.Generic.List<int>(SendTo);
            SendTo.Clear();
            SendWorld(s, gm, targets);
        }

        /// <summary>Host: refresh the slot from the live game (as entering a dungeon does) and send it.</summary>
        private static void SendWorld(CoopLink s, GameManager gm, System.Collections.Generic.List<int> targets)
        {
            try
            {
                var save = Traverse.Create(gm).Field("OnSaveToDungeon").GetValue() as Delegate;
                save?.DynamicInvoke(gm.currentGameSlot);
                FsJSONManager.Serialize(gm.currentGameSlot, out fsData data);
                string json = data.ToString();
                var packed = PackedText.Pack(json);
                foreach (var t in targets) s.SendGameTo(t, MessageType.WorldSlot, new WorldSlot { Data = packed });
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
                    Dungeon.TravelSync.RequestCatchUp();
                    CoopToast.Show("You're in the host's world");
                    CoopPlugin.Log.LogInfo("Co-op: playing in the host's world");
                }
                return;
            }
            if (_pendingJson == null) return;

            // In a game: go to the main menu first (the game only loads a slot from there).
            if (HeroMerchant.Instance != null && (gm.IsTownSceneLoaded() || gm.IsDungeonSceneLoaded()))
            {
                // Already on the way. The game ignores the request while a scene loads: try again.
                if (KeepSessionOnMenu && Time.realtimeSinceStartup - _menuRequestedAt < 10f) return;
                KeepSessionOnMenu = true;
                _menuRequestedAt = Time.realtimeSinceStartup;
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
            if (own == null)
            {
                // Without a copy of their own save the guest would play with an empty bag and
                // could keep nothing: don't join.
                CoopToast.Show("Can't join: load your own save first, then join from the pause menu");
                CoopPlugin.Log.LogWarning("Co-op: no copy of your own save; leaving instead of joining with an empty bag");
                CoopManager.Instance?.Leave();
                return false;
            }
            world.willInventory = new List<ItemSaveInfo>(own.willInventory);
            world.willEquippedItems = new List<ItemSaveInfo>(own.willEquippedItems);
            world.equippedWeaponSet = own.equippedWeaponSet;
            world.equippedFamiliar = own.equippedFamiliar;
            world.familiarsDiscovered = own.familiarsDiscovered;

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
            // StartGame does nothing unless the menu is open; only then swap in the host's world.
            var menu = UnityEngine.Object.FindObjectOfType<MainMenuPanel>();
            if (menu == null || !Traverse.Create(menu).Field("_open").GetValue<bool>()) return;
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
            GhostMode.ReclaimPile();
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
