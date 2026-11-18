using Coop.Plugin.TestMode;
using UnityEngine;

namespace Coop.Plugin.Runtime
{
    /// <summary>Small IMGUI window (F8 by default) to host, join and leave.</summary>
    internal sealed class CoopWindow : MonoBehaviour
    {
        private const int WindowId = 0x4C0C;
        private bool _visible;
        private Rect _rect = new Rect(40, 40, 380, 230);
        private string _address;

        /// <summary>Set by the Steam layer (Task 8) when Steam hosting is available.</summary>
        public static System.Action HostSteam;

        private void Start()
        {
            _address = CoopConfig.LastHostAddress.Value;
        }

        private void Update()
        {
            if (Input.GetKeyDown(CoopConfig.WindowKey.Value)) _visible = !_visible;
        }

        private void OnGUI()
        {
            if (!_visible) return;
            _rect = GUILayout.Window(WindowId, _rect, Draw, TestClientMode.Active ? "Co-op (TEST CLIENT)" : "Co-op");
        }

        private void Draw(int id)
        {
            var mgr = CoopManager.Instance;
            GUILayout.Label("Status: " + mgr.Status);
            GUILayout.Space(6);

            GUI.enabled = !mgr.IsActive;
            GUILayout.BeginHorizontal();
            GUI.enabled = !mgr.IsActive && HostSteam != null;
            if (GUILayout.Button(new GUIContent("Host (Steam)", TestClientMode.Active ? "Steam not available in test client" : "Invite a Steam friend")))
                HostSteam();
            GUI.enabled = !mgr.IsActive;
            if (GUILayout.Button("Host (IP)")) mgr.HostTcp(CoopConfig.Port.Value);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Address", GUILayout.Width(60));
            _address = GUILayout.TextField(_address ?? "", GUILayout.Width(180));
            if (GUILayout.Button("Join (IP)"))
            {
                CoopConfig.LastHostAddress.Value = _address.Trim();
                mgr.JoinTcp(_address.Trim(), CoopConfig.Port.Value);
            }
            GUILayout.EndHorizontal();

            GUI.enabled = mgr.IsActive;
            if (GUILayout.Button("Leave")) mgr.Leave();
            GUI.enabled = true;

            GUILayout.Label(GUI.tooltip);
            GUILayout.Label($"Port {CoopConfig.Port.Value}. Press {CoopConfig.WindowKey.Value} to close.");
            GUI.DragWindow();
        }
    }
}
