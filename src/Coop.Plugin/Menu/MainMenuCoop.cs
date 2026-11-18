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
    /// Adds "Continue as co-op host" under the main menu's Continue entry. It is a clone of the
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

        public static void Apply(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(MainMenuPanel), nameof(MainMenuPanel.Awake)),
                prefix: new HarmonyMethod(typeof(MainMenuCoop), nameof(AwakePrefix)));
            harmony.Patch(AccessTools.Method(typeof(MainMenuPanel), "PrepareMenuButtons"),
                postfix: new HarmonyMethod(typeof(MainMenuCoop), nameof(MirrorContinue)));
            harmony.Patch(AccessTools.Method(typeof(MainMenuPanel), "CheckContinueAndSelectFirstButton"),
                postfix: new HarmonyMethod(typeof(MainMenuCoop), nameof(MirrorContinue)));
        }

        // Before Awake, so the game adds its selection arrows, fade-in and enable/disable to ours too.
        private static void AwakePrefix(MainMenuPanel __instance)
        {
            try
            {
                var cont = __instance.mainMenuButtons[0];
                var go = UnityEngine.Object.Instantiate(cont, cont.transform.parent, false);
                go.name = "CoopHostContinue";
                go.transform.SetSiblingIndex(cont.transform.GetSiblingIndex() + 1);
                foreach (var c in go.GetComponentsInChildren<Component>(true))
                    if (c != null && c.GetType().Name == "Localize") UnityEngine.Object.DestroyImmediate(c);

                var b = go.GetComponent<Button>();
                b.onClick = new Button.ButtonClickedEvent(); // drop the cloned StartGame listener
                var panel = __instance;
                b.onClick.AddListener(() =>
                {
                    try
                    {
                        _hostPending = true;
                        _readySince = -1f;
                        CoopPlugin.Log.LogInfo("Co-op: loading your save to host");
                        panel.StartGame();
                    }
                    catch (Exception e) { CoopPlugin.Log.LogError("Co-op host from main menu failed: " + e); }
                });
                SetLabel(go, cont);
                MakeRoom(__instance, cont, go);
                // Appended: the game reaches its own buttons by index 0..5.
                __instance.mainMenuButtons.Add(go);
                _button = go;
            }
            catch (Exception e)
            {
                CoopPlugin.Log.LogError("Could not add co-op to the main menu: " + e);
            }
        }

        /// <summary>
        /// The menu entries have fixed positions (no layout group): put ours one row below
        /// Continue and move every entry below Continue down by one row.
        /// </summary>
        private static void MakeRoom(MainMenuPanel panel, GameObject cont, GameObject ours)
        {
            var c = (RectTransform)cont.transform;
            var next = (RectTransform)panel.mainMenuButtons[1].transform;
            float row = next.anchoredPosition.y - c.anchoredPosition.y; // negative: rows go down
            if (Mathf.Abs(row) < 1f) row = -60f;
            foreach (var b in panel.mainMenuButtons)
            {
                if (b == null || b == cont) continue;
                var r = (RectTransform)b.transform;
                if (r.parent == c.parent && r.anchoredPosition.y < c.anchoredPosition.y - 1f)
                    r.anchoredPosition += new Vector2(0f, row);
            }
            var o = (RectTransform)ours.transform;
            o.anchoredPosition = c.anchoredPosition + new Vector2(0f, row);
            LinkNavigation(panel, cont, ours);
        }

        /// <summary>
        /// The menu uses explicit up/down links, and our clone copied Continue's (down = Load),
        /// so keys and pad skipped it. Insert it into the chain: Continue -> ours -> Load.
        /// </summary>
        private static void LinkNavigation(MainMenuPanel panel, GameObject cont, GameObject ours)
        {
            var c = cont.GetComponent<Button>();
            var o = ours.GetComponent<Button>();
            CoopPlugin.Log.LogInfo($"Co-op main menu: Continue navigation was {c.navigation.mode}, down = {(c.navigation.selectOnDown != null ? c.navigation.selectOnDown.name : "none")}");
            var below = c.navigation.selectOnDown;
            if (below == null || below == o) below = panel.mainMenuButtons[1].GetComponent<Selectable>();

            var nc = c.navigation;
            nc.mode = Navigation.Mode.Explicit;
            nc.selectOnDown = o;
            c.navigation = nc;

            var no = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = c, selectOnDown = below };
            o.navigation = no;

            if (below != null)
            {
                var nb = below.navigation;
                if (nb.mode == Navigation.Mode.Explicit && (nb.selectOnUp == c || nb.selectOnUp == null))
                {
                    nb.selectOnUp = o;
                    below.navigation = nb;
                }
            }
        }

        // Continue is hidden without a save: then ours is hidden too, and Up from Load must not
        // stop on Continue. The game hides Continue in PrepareMenuButtons, after Awake.
        private static void FixNavigationWhenHidden(MainMenuPanel panel)
        {
            if (_button == null || panel.mainMenuButtons[0].activeSelf) return;
            var load = panel.mainMenuButtons[1].GetComponent<Selectable>();
            var nl = load.navigation;
            if (nl.mode == Navigation.Mode.Explicit && nl.selectOnUp == _button.GetComponent<Selectable>())
            {
                nl.selectOnUp = null;
                load.navigation = nl;
            }
        }

        private static void SetLabel(GameObject go, GameObject continueButton)
        {
            var text = go.GetComponent<Text>() ?? go.GetComponentInChildren<Text>(true);
            if (text == null) return;
            var original = continueButton.GetComponent<Text>() ?? continueButton.GetComponentInChildren<Text>(true);
            bool upper = original != null && !string.IsNullOrEmpty(original.text) && original.text == original.text.ToUpperInvariant();
            text.text = upper ? Label.ToUpperInvariant() : Label;
        }

        // Shown and clickable exactly when Continue is.
        private static void MirrorContinue(MainMenuPanel __instance)
        {
            if (_button == null) return;
            var cont = __instance.mainMenuButtons[0];
            _button.SetActive(cont.activeSelf);
            _button.GetComponent<Button>().interactable = cont.GetComponent<Button>().interactable;
            SetLabel(_button, cont);
            FixNavigationWhenHidden(__instance);
        }

        /// <summary>Called every frame: starts hosting once the loaded save reaches town.</summary>
        public static void Tick()
        {
            if (!_hostPending) return;
            var gm = GameManager.Instance;
            bool ready = gm != null && gm.IsTownSceneLoaded() && HeroMerchant.Instance != null;
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
