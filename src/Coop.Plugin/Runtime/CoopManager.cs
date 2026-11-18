using System;
using System.Net.Sockets;
using Coop.Core.Net;
using Coop.Core.Protocol;
using Coop.Core.Session;
using Coop.Plugin.TestMode;
using DigitalSunGames.Platforms;
using UnityEngine;

namespace Coop.Plugin.Runtime
{
    /// <summary>
    /// Owns the current co-op connection. Lives for the whole game (DontDestroyOnLoad)
    /// and pumps the session once per frame.
    /// </summary>
    internal sealed class CoopManager : MonoBehaviour
    {
        /// <summary>
        /// After the session closes on its own (reject, timeout), the link stays open this long
        /// so the last reliable message (e.g. the reject reason) still reaches the other side.
        /// </summary>
        public const float LingerSeconds = 1f;

        public static CoopManager Instance { get; private set; }

        public PeerSession Session { get; private set; }
        public string Status { get; private set; } = "Not connected";
        public bool IsActive => Session != null && Session.State != SessionState.Closed;

        public event Action SessionStarted;
        public event Action<string> SessionClosed;

        private ITransport _transport;
        private ITransport _lingering;
        private float _lingerUntil;
        private string _lingerReason;
        private bool _autoArgsHandled;

        public static void Create()
        {
            var go = new GameObject("@MoonlighterCoop");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            Instance = go.AddComponent<CoopManager>();
            go.AddComponent<CoopToast>();
            go.AddComponent<Sync.PlayerSync>();
            go.AddComponent<Shared.GhostMode>();
        }

        /// <summary>
        /// Launch options for testing without clicking: --coop-auto-host, or
        /// --coop-auto-join &lt;address&gt;. Both use the configured port. Handled once the
        /// game's platform layer is up, so the DLC list in the handshake is correct.
        /// </summary>
        private void HandleAutoArgs()
        {
            if (_autoArgsHandled || Platform.DLC == null) return;
            _autoArgsHandled = true;
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
            Begin(t, $"Hosting on port {port}, waiting for the other player...");
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
            Begin(t, $"Connecting to {address}:{port}...");
        }

        public void StartWith(ITransport transport, string status) => Begin(transport, status);

        /// <summary>Leave on purpose: tell the other player and close at once.</summary>
        public void Leave()
        {
            if (Session != null && Session.State != SessionState.Closed)
                Session.Close("You left");
            DisposeTransport(ref _transport);
            FinishLinger();
        }

        private void Begin(ITransport transport, string status)
        {
            _transport = transport;
            Session = new PeerSession(transport, BuildHello(), TestClientMode.Active, () => Time.realtimeSinceStartup);
            Session.StateChanged += OnStateChanged;
            Status = status;
            CoopPlugin.Log.LogInfo(status);
            Dungeon.FloorSync.Attach(Session);
            Dungeon.TravelSync.Attach(Session);
            Dungeon.EnemySync.Attach(Session);
            Shared.GoldSync.Attach(Session);
            Shared.GhostMode.Attach(Session);
            Shared.DaySync.Attach(Session);
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
                // Keep the link open briefly so a final Reject/Bye is delivered, then clean up.
                FinishLinger();
                _lingering = _transport;
                _transport = null;
                _lingerUntil = Time.realtimeSinceStartup + LingerSeconds;
                _lingerReason = Session.CloseReason;
            }
        }

        private void FinishLinger()
        {
            if (_lingerReason == null && _lingering == null) return;
            DisposeTransport(ref _lingering);
            var reason = _lingerReason;
            _lingerReason = null;
            if (reason != null) SessionClosed?.Invoke(reason);
        }

        private static void DisposeTransport(ref ITransport t)
        {
            if (t == null) return;
            try { t.Dispose(); }
            catch (Exception e) { CoopPlugin.Log.LogWarning("Transport dispose failed: " + e.Message); }
            t = null;
        }

        private void Update()
        {
            if (!TestClientMode.Active) Steam.SteamLobby.EnsureInitialized();
            HandleAutoArgs();
            Dungeon.EnemySync.Tick();
            Shared.GoldSync.Tick();
            Shared.DaySync.Tick();

            if (_lingering != null || _lingerReason != null)
            {
                try { _lingering?.Poll(); } catch (Exception) { /* closing anyway */ }
                if (Time.realtimeSinceStartup >= _lingerUntil) FinishLinger();
            }

            if (Session == null || _transport == null) return;
            try
            {
                Session.Tick();
            }
            catch (Exception e)
            {
                CoopPlugin.Log.LogError("Co-op session error: " + e);
                try { Session.Close("Error: " + e.Message); }
                catch (Exception) { DisposeTransport(ref _transport); }
            }
        }

        private void OnApplicationQuit() => Leave();
    }
}
