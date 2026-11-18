using UnityEngine;

namespace Coop.Plugin.Runtime
{
    /// <summary>
    /// Display-only notice at the top of the screen when the co-op status changes
    /// (connected, disconnected...). Hosting and joining live in the pause menu.
    /// </summary>
    internal sealed class CoopToast : MonoBehaviour
    {
        private const float ShowSeconds = 4f;
        private string _text;
        private float _until;
        private string _last;
        private GUIStyle _style;

        private static CoopToast _instance;

        private void Awake() => _instance = this;

        /// <summary>Shows a one-off notice at the top of the screen.</summary>
        public static void Show(string text)
        {
            if (_instance == null) return;
            _instance._text = text;
            _instance._until = Time.unscaledTime + ShowSeconds;
        }

        private void Update()
        {
            var status = CoopManager.Instance != null ? CoopManager.Instance.Status : null;
            if (status != null && status != _last)
            {
                _last = status;
                _text = "Co-op: " + status;
                _until = Time.unscaledTime + ShowSeconds;
            }
        }

        private void OnGUI()
        {
            if (_text == null || Time.unscaledTime > _until) return;
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.box) { fontSize = 18, alignment = TextAnchor.MiddleCenter, wordWrap = true };
                _style.normal.textColor = new Color(0.85f, 0.95f, 0.8f);
            }
            float w = Mathf.Min(620, Screen.width - 40);
            GUI.Box(new Rect((Screen.width - w) / 2, 20, w, 40), _text, _style);
        }
    }
}
