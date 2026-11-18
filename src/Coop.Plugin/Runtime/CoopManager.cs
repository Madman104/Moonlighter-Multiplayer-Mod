using System;
using System.Net.Sockets;
using Coop.Core.Net;
using Coop.Core.Protocol;
using Coop.Core.Session;
using Coop.Plugin.TestMode;
using UnityEngine;

namespace Coop.Plugin.Runtime
{
    /// <summary>
    /// Owns the current co-op connection. Lives for the whole game (DontDestroyOnLoad)
    /// and pumps the session once per frame.
    /// </summary>
    internal sealed class CoopManager : MonoBehaviour
    {
        public static CoopManager Instance { get; private set; }

        public PeerSession Session { get; private set; }
        public string Status { get; private set; } = "Not connected";
        public bool IsActive => Session != null && Session.State != SessionState.Closed;

        public event Action SessionStarted;
        public event Action<string> SessionClosed;

        private ITransport _transport;

        public static void Create()
        {
            var go = new GameObject("@MoonlighterCoop");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            Instance = go.AddComponent<CoopManager>();
            go.AddComponent<CoopWindow>();
            go.AddComponent<Sync.PlayerSync>();
        }

        /// <summary>
        /// Launch options for testing without clicking: --coop-auto-host, or
        /// --coop-auto-join &lt;address&gt;. Both use the configured port.
        /// </summary>
        private void Awake()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--coop-auto-host", StringComparison.OrdinalIgnoreCase))
                    HostTcp(CoopConfig.Port.Value);
                else if (string.Equals(args[i], "--coop-auto-join", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    JoinTcp(args[i + 1], CoopConfig.Port.Value);
            }
        }

        public void HostTcp(int port)
        {
            Leave();
            TcpTransport t;
            try
            {
                t = TcpTransport.Host(port);
            }
            catch (SocketException e)
            {
                Status = $"Could not host on port {port}: {e.Message}";
                CoopPlugin.Log.LogWarning(Status);
                return;
            }
            // Same-PC test connections share this install, so their DLC matches by definition.
            t.Connected += () => { if (Session != null) Session.IgnoreDlc = t.RemoteIsLoopback; };
            Start(t, $"Hosting on port {port}, waiting for the other player...");
        }

        public void JoinTcp(string address, int port)
        {
            Leave();
            TcpTransport t;
            try
            {
                t = TcpTransport.Connect(address, port);
            }
            catch (Exception e) when (e is SocketException || e is ArgumentException)
            {
                Status = $"Could not join {address}: {e.Message}";
                CoopPlugin.Log.LogWarning(Status);
                return;
            }
            Start(t, $"Connecting to {address}:{port}...");
        }

        public void StartWith(ITransport transport, string status) => Start(transport, status);

        public void Leave()
        {
            if (Session != null && Session.State != SessionState.Closed)
                Session.Close("You left");
            Teardown();
        }

        private void Start(ITransport transport, string status)
        {
            _transport = transport;
            Session = new PeerSession(transport, BuildHello(), TestClientMode.Active, () => Time.realtimeSinceStartup);
            Session.StateChanged += OnStateChanged;
            Status = status;
            CoopPlugin.Log.LogInfo(status);
            SessionStarted?.Invoke();
        }

        private static Hello BuildHello() => new Hello
        {
            ModVersion = CoopPlugin.ModVersion,
            GameVersion = GameInfo.GameVersion,
            OwnedDlc = GameInfo.OwnedDlcNames(),
        };

        private void OnStateChanged(SessionState state)
        {
            switch (state)
            {
                case SessionState.Handshaking:
                    Status = "Checking versions...";
                    break;
                case SessionState.Connected:
                    Status = Session.IsHost ? "Connected (you are the host)" : "Connected (you joined)";
                    break;
                case SessionState.Closed:
                    Status = "Closed: " + Session.CloseReason;
                    break;
            }
            CoopPlugin.Log.LogInfo("Co-op: " + Status);
            if (state == SessionState.Closed)
            {
                var reason = Session.CloseReason;
                Teardown();
                SessionClosed?.Invoke(reason);
            }
        }

        private void Teardown()
        {
            if (_transport != null)
            {
                try { _transport.Dispose(); }
                catch (Exception e) { CoopPlugin.Log.LogWarning("Transport dispose failed: " + e.Message); }
                _transport = null;
            }
        }

        private void Update()
        {
            if (!TestClientMode.Active) Steam.SteamLobby.EnsureInitialized();
            if (Session == null || _transport == null) return;
            try
            {
                Session.Tick();
            }
            catch (Exception e)
            {
                CoopPlugin.Log.LogError("Co-op session error: " + e);
                Session.Close("Error: " + e.Message);
            }
        }

        private void OnApplicationQuit() => Leave();
    }
}
