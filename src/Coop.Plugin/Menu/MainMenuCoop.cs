using System;
using Coop.Plugin.Runtime;
using Coop.Plugin.Steam;
using Coop.Plugin.TestMode;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Coop.Plugin.Menu
{
    /// <summary>
    /// Adds "Continue as co-op host" and "Join co-op" under the main menu's Continue entry. It is a clone of the
    /// game's own Continue button (same font, fade-in and selection arrows). Choosing it loads
    /// your save exactly like Continue, then starts hosting (Steam lobby; IP in test-client mode)
    /// once you are in town.
    /// </summary>
    internal static class MainMenuCoop
    {
        private const string Label = "Continue as co-op host";
        private const float HostDelay = 2f; // let the town finish appearing first

        private static GameObject _button;
        private static bool _hostPending;
        private static float _readySince = -1f;
        private static float _clickedAt;

        public static void Apply(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(MainMenuPanel), nameof(MainMenuPanel.Awake)),
                prefix: new HarmonyMethod(typeof(MainMenuCoop), nameof(AwakePrefix)));
            harmony.Patch(AccessTools.Method(typeof(MainMenuPanel), "PrepareMenuButtons"),
                postfix: new HarmonyMethod(typeof(MainMenuCoop), nameof(MirrorContinue)));
            harmony.Patch(AccessTools.Method(typeof(MainMenuPanel), "CheckContinueAndSelectFirstButton"),
                postfix: new HarmonyMethod(typeof(MainMenuCoop), nameof(MirrorContinue)));
        }

        private const string JoinLabel = "Join co-op";
        private static GameObject _join;
        private static Selectable _below;

        // Before Awake, so the game adds its selection arrows, fade-in and enable/disable to ours too.
        private static void AwakePrefix(MainMenuPanel __instance)
        {
            try
            {
                var panel = __instance;
                var cont = panel.mainMenuButtons[0];
                _button = CloneEntry(cont, "CoopHostContinue", 1, () =>
                {
                    _hostPending = true;
                    _readySince = -1f;
                    _clickedAt = Time.realtimeSinceStartup;
                    CoopPlugin.Log.LogInfo("Co-op: loading your save to host");
                    panel.StartGame();
                });
                _join = CloneEntry(cont, "CoopJoin", 2, JoinNow);
                SetLabel(_button, cont, Label);
                SetLabel(_join, cont, JoinLabel);
                MakeRoom(panel, cont, 2);
                LinkNavigation(panel, cont);
                // Appended: the game reaches its own buttons by index 0..5.
                panel.mainMenuButtons.Add(_button);
                panel.mainMenuButtons.Add(_join);
            }
            catch (Exception e)
            {
                CoopPlugin.Log.LogError("Could not add co-op to the main menu: " + e);
            }
        }

        private static GameObject CloneEntry(GameObject cont, string name, int rowsBelow, Action onClick)
        {
            var go = UnityEngine.Object.Instantiate(cont, cont.transform.parent, false);
            go.name = name;
            go.transform.SetSiblingIndex(cont.transform.GetSiblingIndex() + rowsBelow);
            foreach (var c in go.GetComponentsInChildren<Component>(true))
                if (c != null && c.GetType().Name == "Localize") UnityEngine.Object.DestroyImmediate(c);
            var b = go.GetComponent<Button>();
            b.onClick = new Button.ButtonClickedEvent(); // drop the cloned StartGame listener
            b.onClick.AddListener(() =>
            {
                try { onClick(); }
                catch (Exception e) { CoopPlugin.Log.LogError("Co-op main menu action failed: " + e); }
            });
            return go;
        }

        // Join: Steam players accept the host's invite (the overlay opens on the friends list,
        // which works with a pad); without Steam, join the last IP address.
        private static void JoinNow()
        {
            var mgr = CoopManager.Instance;
            if (mgr == null) return;
            if (!TestClientMode.Active && SteamLobby.Available)
            {
                SteamLobby.OpenFriends();
                CoopToast.Show("Accept your friend's co-op invite in Steam");
                return;
            }
            CoopToast.Show("Joining " + CoopConfig.LastHostAddress.Value + "...");
            mgr.JoinTcp(CoopConfig.LastHostAddress.Value, CoopConfig.Port.Value);
        }

        private static float Row(MainMenuPanel panel, GameObject cont)
        {
            var c = (RectTransform)cont.transform;
            var next = (RectTransform)panel.mainMenuButtons[1].transform;
            float row = next.anchoredPosition.y - c.anchoredPosition.y; // negative: rows go down
            return Mathf.Abs(row) < 1f ? -60f : row;
        }

        /// <summary>
        /// The menu entries have fixed positions (no layout group): put ours in the rows below
        /// Continue and move every entry below Continue down to make room.
        /// </summary>
        private static void MakeRoom(MainMenuPanel panel, GameObject cont, int rows)
        {
            var c = (RectTransform)cont.transform;
            float row = Row(panel, cont);
            foreach (var b in panel.mainMenuButtons)
            {
                if (b == null || b == cont) continue;
                var r = (RectTransform)b.transform;
                if (r.parent == c.parent && r.anchoredPosition.y < c.anchoredPosition.y - 1f)
                    r.anchoredPosition += new Vector2(0f, row * rows);
            }
            ((RectTransform)_button.transform).anchoredPosition = c.anchoredPosition + new Vector2(0f, row);
            ((RectTransform)_join.transform).anchoredPosition = c.anchoredPosition + new Vector2(0f, row * 2f);
        }

        private static Navigation Nav(Selectable up, Selectable down)
            => new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = up, selectOnDown = down };

        /// <summary>
        /// The menu uses explicit up/down links (Continue's down is Load), so clones would be
        /// skipped by keys and pad. Chain: Continue -> host -> join -> Load.
        /// </summary>
        private static void LinkNavigation(MainMenuPanel panel, GameObject cont)
        {
            var c = cont.GetComponent<Button>();
            var h = _button.GetComponent<Button>();
            var j = _join.GetComponent<Button>();
            var below = c.navigation.selectOnDown;
            if (below == null || below == h || below == j) below = panel.mainMenuButtons[1].GetComponent<Selectable>();
            _below = below;

            var nc = c.navigation;
            nc.mode = Navigation.Mode.Explicit;
            nc.selectOnDown = h;
            c.navigation = nc;
            h.navigation = Nav(c, j);
            j.navigation = Nav(h, below);
            if (below != null)
            {
                var nb = below.navigation;
                if (nb.mode == Navigation.Mode.Explicit)
                {
                    nb.selectOnUp = j;
                    below.navigation = nb;
                }
            }
        }

        // Without a save Continue (and our host entry) is hidden: Join links straight to Load.
        private static void FixNavigationWhenHidden(MainMenuPanel panel)
        {
            if (_join == null) return;
            bool contShown = panel.mainMenuButtons[0].activeSelf;
            var j = _join.GetComponent<Button>();
            j.navigation = Nav(contShown ? (Selectable)_button.GetComponent<Button>() : null, _below);
        }

        private static void SetLabel(GameObject go, GameObject continueButton, string label)
        {
            var text = go.GetComponent<Text>() ?? go.GetComponentInChildren<Text>(true);
            if (text == null) return;
            var original = continueButton.GetComponent<Text>() ?? continueButton.GetComponentInChildren<Text>(true);
            bool upper = original != null && !string.IsNullOrEmpty(original.text) && original.text == original.text.ToUpperInvariant();
            text.text = upper ? label.ToUpperInvariant() : label;
        }

        // The host entry is shown and clickable exactly when Continue is; Join always.
        private static void MirrorContinue(MainMenuPanel __instance)
        {
            if (_button == null) return;
            var cont = __instance.mainMenuButtons[0];
            _button.SetActive(cont.activeSelf);
            _button.GetComponent<Button>().interactable = cont.GetComponent<Button>().interactable;
            SetLabel(_button, cont, Label);
            if (_join != null)
            {
                _join.SetActive(true);
                SetLabel(_join, cont, JoinLabel);
            }
            FixNavigationWhenHidden(__instance);
        }

        /// <summary>Called every frame: starts hosting once the loaded save reaches town.</summary>
        public static void Tick()
        {
            if (!_hostPending) return;
            var gm = GameManager.Instance;
            bool ready = gm != null && gm.IsTownSceneLoaded() && HeroMerchant.Instance != null;
            // Still on the main menu long after the click: the load didn't start, forget it.
            if (!ready && HeroMerchant.Instance == null && UnityEngine.Object.FindObjectOfType<MainMenuPanel>() != null
                && Time.realtimeSinceStartup - _clickedAt > 20f) { _hostPending = false; return; }
            if (!ready) { _readySince = -1f; return; }
            if (_readySince < 0f) { _readySince = Time.realtimeSinceStartup; return; }
            if (Time.realtimeSinceStartup - _readySince < HostDelay) return;

            _hostPending = false;
            var mgr = CoopManager.Instance;
            if (mgr == null || mgr.IsActive) return;
            if (!TestClientMode.Active && SteamLobby.Available)
            {
                SteamLobby.HostNow();
                CoopToast.Show("Hosting co-op - pick a friend in the Steam invite window");
            }
            else
            {
                mgr.HostTcp(CoopConfig.Port.Value);
                CoopToast.Show("Hosting co-op on port " + CoopConfig.Port.Value);
            }
        }
    }
}
