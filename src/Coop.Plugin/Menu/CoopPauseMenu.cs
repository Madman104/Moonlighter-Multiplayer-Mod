using System;
using System.Collections.Generic;
using System.Linq;
using Coop.Core.Net;
using Coop.Plugin.Runtime;
using Coop.Plugin.Steam;
using Coop.Plugin.TestMode;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Coop.Plugin.Menu
{
    /// <summary>
    /// Adds a CO-OP entry to the game's pause menu. Selecting it switches the same frame
    /// to a co-op sub-menu. All buttons are clones of the game's own pause buttons, so they
    /// use its font, colours and selection arrows, and work with mouse, keyboard and pad.
    /// </summary>
    internal sealed class CoopPauseMenu : MonoBehaviour
    {
        private PausePanel _panel;
        private RectTransform _container;
        private readonly List<Button> _original = new List<Button>();
        private Button _entry;
        private Button _status, _hostSteam, _hostIp, _join, _paste, _leave, _back;
        private readonly List<Button> _sub = new List<Button>();
        private string _pasteMessage;
        private float _pasteMessageUntil;

        public bool SubMenuOpen { get; private set; }

        public static void Apply(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(PausePanel), "Awake"),
                prefix: new HarmonyMethod(typeof(CoopPauseMenu), nameof(AwakePrefix)));
            harmony.Patch(AccessTools.Method(typeof(PausePanel), nameof(PausePanel.Enable)),
                prefix: new HarmonyMethod(typeof(CoopPauseMenu), nameof(EnablePrefix)));
            harmony.Patch(AccessTools.Method(typeof(PausePanel), nameof(PausePanel.OnBButtonPressed)),
                prefix: new HarmonyMethod(typeof(CoopPauseMenu), nameof(BackPrefix)));
            harmony.Patch(AccessTools.Method(typeof(PausePanel), nameof(PausePanel.OnYButtonPressed)),
                prefix: new HarmonyMethod(typeof(CoopPauseMenu), nameof(BackPrefix)));
        }

        // Runs before PausePanel.Awake, so the game hooks its selection arrows onto our buttons too.
        private static void AwakePrefix(PausePanel __instance)
        {
            try
            {
                if (__instance.GetComponent<CoopPauseMenu>() != null) return;
                __instance.gameObject.AddComponent<CoopPauseMenu>().Install(__instance);
            }
            catch (Exception e)
            {
                CoopPlugin.Log.LogError("Could not add co-op to the pause menu: " + e);
            }
        }

        // Every time the pause menu opens, start on the normal page.
        private static void EnablePrefix(PausePanel __instance)
        {
            var menu = __instance.GetComponent<CoopPauseMenu>();
            if (menu != null && menu.SubMenuOpen) menu.ShowSubMenu(false, resizeFrame: false);
        }

        // Back (B / Esc-style) inside the co-op page returns to the pause page instead of resuming.
        private static bool BackPrefix(PausePanel __instance, ref bool __result)
        {
            var menu = __instance.GetComponent<CoopPauseMenu>();
            if (menu == null || !menu.SubMenuOpen || !__instance.receiveInput) return true;
            menu.ShowSubMenu(false, resizeFrame: true);
            __result = true;
            return false;
        }

        private void Install(PausePanel panel)
        {
            _panel = panel;
            _original.AddRange(panel.buttons.Where(b => b != null));
            if (_original.Count == 0) throw new InvalidOperationException("pause menu has no buttons");

            var template = _original[0];
            _container = (RectTransform)template.transform.parent;
            LogHierarchy(template);

            // CO-OP goes just above the last original entry (Exit).
            int entryIndex = _original[_original.Count - 1].transform.GetSiblingIndex();
            _entry = Clone(template, "CO-OP", () => ShowSubMenu(true, resizeFrame: true));
            _entry.transform.SetSiblingIndex(entryIndex);

            _status = Clone(template, "", null);
            _status.interactable = false;
            _status.transition = Selectable.Transition.None;
            _hostSteam = Clone(template, "HOST (STEAM)", () => { if (SteamLobby.Available) SteamLobby.HostNow(); });
            _hostIp = Clone(template, "HOST (IP)", () => CoopManager.Instance.HostTcp(CoopConfig.Port.Value));
            _join = Clone(template, "JOIN", () => CoopManager.Instance.JoinTcp(CoopConfig.LastHostAddress.Value, CoopConfig.Port.Value));
            _paste = Clone(template, "PASTE IP", PasteAddress);
            _leave = Clone(template, "LEAVE CO-OP", () => CoopManager.Instance.Leave());
            _back = Clone(template, "BACK", () => ShowSubMenu(false, resizeFrame: true));
            _sub.AddRange(new[] { _status, _hostSteam, _hostIp, _join, _paste, _leave, _back });
            foreach (var b in _sub) b.gameObject.SetActive(false);

            // Append to the list (never insert): the game uses buttons[0] and buttons[2] by index.
            panel.buttons.Add(_entry);
            panel.buttons.AddRange(_sub.Where(b => b != _status));

            RebuildNavigation();
            CoopPlugin.Log.LogInfo("Co-op added to the pause menu");
        }

        private Button Clone(Button template, string label, Action onClick)
        {
            var go = Instantiate(template.gameObject, template.transform.parent, false);
            go.name = "Coop_" + (label.Length > 0 ? label.Replace(" ", "") : "Status");
            // The game's localizer would overwrite our text every time the language refreshes.
            foreach (var c in go.GetComponentsInChildren<Component>(true))
                if (c != null && c.GetType().Name == "Localize") DestroyImmediate(c);

            var b = go.GetComponent<Button>();
            b.onClick = new Button.ButtonClickedEvent(); // drop the cloned persistent listener
            if (onClick != null)
                b.onClick.AddListener(() =>
                {
                    if (!_panel.receiveInput) return;
                    try { onClick(); }
                    catch (Exception e) { CoopPlugin.Log.LogError("Co-op menu action failed: " + e); }
                });
            SetLabel(b, label);
            return b;
        }

        private static void SetLabel(Button b, string text)
        {
            var tmp = b.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null) { tmp.text = text; return; }
            var ui = b.GetComponentInChildren<Text>(true);
            if (ui != null) ui.text = text;
        }

        public void ShowSubMenu(bool open, bool resizeFrame)
        {
            SubMenuOpen = open;
            foreach (var b in _original) b.gameObject.SetActive(!open);
            _entry.gameObject.SetActive(!open);
            foreach (var b in _sub) b.gameObject.SetActive(open);
            Refresh();
            RebuildNavigation();
            if (resizeFrame) ResizeFrame();

            if (_panel.receiveInput || !resizeFrame)
            {
                var first = open ? FirstInteractable() : _entry;
                if (first != null && resizeFrame)
                {
                    EventSystem.current?.SetSelectedGameObject(null);
                    first.Select();
                }
            }
        }

        private Button FirstInteractable()
        {
            return _sub.FirstOrDefault(b => b.gameObject.activeSelf && b.interactable) ?? _back;
        }

        private void Update()
        {
            if (SubMenuOpen) Refresh();
        }

        private void Refresh()
        {
            var mgr = CoopManager.Instance;
            bool active = mgr != null && mgr.IsActive;

            string status = mgr != null ? mgr.Status : "";
            if (_pasteMessage != null && Time.unscaledTime < _pasteMessageUntil) status = _pasteMessage;
            SetLabel(_status, Shorten(status, 46));

            SetInteractable(_hostSteam, !active && SteamLobby.Available && !TestClientMode.Active);
            SetInteractable(_hostIp, !active);
            SetInteractable(_join, !active);
            SetInteractable(_paste, !active);
            SetInteractable(_leave, active);
            SetLabel(_join, "JOIN " + CoopConfig.LastHostAddress.Value);
        }

        private void SetInteractable(Button b, bool value)
        {
            if (b.interactable == value) return;
            bool wasSelected = EventSystem.current != null && EventSystem.current.currentSelectedGameObject == b.gameObject;
            b.interactable = value;
            RebuildNavigation();
            if (wasSelected && !value) FirstInteractable()?.Select();
        }

        private void PasteAddress()
        {
            var clip = GUIUtility.systemCopyBuffer;
            if (HostAddress.TryNormalize(clip, out var address))
            {
                CoopConfig.LastHostAddress.Value = address;
                _pasteMessage = "Address set to " + address;
            }
            else
            {
                _pasteMessage = "Copy the host's IP address first";
            }
            _pasteMessageUntil = Time.unscaledTime + 3f;
        }

        /// <summary>Up/down moves through the visible, usable buttons and wraps around.</summary>
        private void RebuildNavigation()
        {
            var visible = new List<Button>();
            for (int i = 0; i < _container.childCount; i++)
            {
                var b = _container.GetChild(i).GetComponent<Button>();
                if (b != null && b.gameObject.activeSelf && b.interactable) visible.Add(b);
            }
            for (int i = 0; i < visible.Count; i++)
            {
                var nav = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnUp = visible[(i - 1 + visible.Count) % visible.Count],
                    selectOnDown = visible[(i + 1) % visible.Count],
                };
                visible[i].navigation = nav;
            }
        }

        /// <summary>Same sizing the game uses when it opens the menu, applied instantly.</summary>
        private void ResizeFrame()
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(_container);
            var mask = _panel.rectMask;
            var content = mask.transform.GetChild(0).GetComponent<RectTransform>();
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            float height = content.sizeDelta.y + 15f + 15f;
            _panel.rectTop.anchoredPosition = new Vector2(_panel.rectTop.anchoredPosition.x, height * 0.5f);
            _panel.rectBot.anchoredPosition = new Vector2(_panel.rectBot.anchoredPosition.x, -height * 0.5f);
            mask.sizeDelta = new Vector2(mask.sizeDelta.x, height);
        }

        private static string Shorten(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max - 3) + "...";
        }

        private static void LogHierarchy(Button template)
        {
            var parts = new List<string>();
            for (var t = template.transform; t != null; t = t.parent)
                parts.Insert(0, t.name + "[" + string.Join(",", t.GetComponents<Component>().Select(c => c == null ? "?" : c.GetType().Name).ToArray()) + "]");
            CoopPlugin.Log.LogDebug("Pause button path: " + string.Join(" > ", parts.ToArray()));
        }
    }
}
