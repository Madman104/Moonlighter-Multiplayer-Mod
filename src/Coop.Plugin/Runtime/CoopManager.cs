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

        public static CoopManager Instance { get; private set; }

        public CoopLink Session { get; private set; }
        public string Status { get; private set; } = "Not connected";
        public bool IsActive => Session != null && Session.State != SessionState.Closed;

        public event Action SessionStarted;
        public event Action<string> SessionClosed;

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
            UpdateCheck.Start(Instance);
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
            TcpGuestListener l;
            try
            {
                l = new TcpGuestListener(port);
            }
            catch (SocketException e)
            {
                Status = $"Could not host on port {port}: {e.Message}";
                CoopPlugin.Log.LogWarning(Status);
                return;
            }
            StartHost(l, $"Hosting on port {port}, waiting for players (up to {CoopLink.MaxPlayers - 1} can join)...",
                t => t is TcpTransport tcp && tcp.RemoteIsLoopback); // same-PC tests share this install's DLC
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
            Begin(CoopLink.Guest(t, BuildHello(), TestClientMode.Active, () => Time.realtimeSinceStartup), $"Connecting to {address}:{port}...");
        }

        /// <summary>Join as a guest over an existing transport (Steam).</summary>
        public void StartWith(ITransport transport, string status)
            => Begin(CoopLink.Guest(transport, BuildHello(), TestClientMode.Active, () => Time.realtimeSinceStartup), status);

        /// <summary>Host for up to three guests arriving through the listener (Steam or TCP).</summary>
        public void StartHost(IGuestListener listener, string status, Func<ITransport, bool> ignoreDlcFor = null)
        {
            var link = CoopLink.Host(listener, BuildHello(), TestClientMode.Active, () => Time.realtimeSinceStartup);
            link.IgnoreDlcFor = ignoreDlcFor;
            Begin(link, status);
        }

        /// <summary>Leave on purpose (guest) or stop hosting (host): tell the others and close.</summary>
        public void Leave()
        {
            if (Session != null && Session.State != SessionState.Closed)
                Session.Close("You left");
        }

        private void Begin(CoopLink link, string status)
        {
            Session = link;
            Session.StateChanged += OnStateChanged;
            Session.PeerJoined += i => { Status = $"{Session.PeerCount + 1} players"; CoopPlugin.Log.LogInfo($"Co-op: player {i + 1} joined"); };
            Session.PeerLeft += i =>
            {
                Status = Session.PeerCount > 0 ? $"{Session.PeerCount + 1} players" : "Hosting, waiting for players...";
                CoopPlugin.Log.LogInfo($"Co-op: player {i + 1} left");
                CoopToast.Show(PlayerNames.Name(i) + " left");
            };
            Status = status;
            CoopPlugin.Log.LogInfo(status);
            PlayerNames.Attach(Session);
            Dungeon.FloorSync.Attach(Session);
            Dungeon.TravelSync.Attach(Session);
            Dungeon.EnemySync.Attach(Session);
            Dungeon.EnemyMirror.Attach(Session);
            Sync.PartnerSounds.Attach(Session);
            Sync.SpriteTransfer.Attach(Session);
            Dungeon.EnemyHitGuard.Attach(Session);
            Dungeon.ProjectileMirror.Attach(Session);
            Dungeon.SummonSync.Attach(Session);
            Dungeon.FloorItems.Attach(Session);
            Dungeon.BreakableSync.Attach(Session);
            Dungeon.ChestSync.Attach(Session);
            Dungeon.RoomObjectSync.Attach(Session);
            Dungeon.WaveSync.Attach(Session);
            Dungeon.LateApply.Clear();
            Shared.GoldSync.Attach(Session);
            Shared.GhostMode.Attach(Session);
            Shared.DaySync.Attach(Session);
            Shared.WorldSync.Attach(Session);
            Shared.ShopSync.Attach(Session);
            Shared.TownSync.Attach(Session);
            Shared.CitizenMirror.Attach(Session);
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
                    Status = Session.IsHost ? "Hosting, waiting for players..." : "Checking versions...";
                    break;
                case SessionState.Connected:
                    Status = Session.IsHost ? $"{Session.PeerCount + 1} players (you are the host)" : "Connected (you joined)";
                    if (!Session.IsHost) Shared.SaveGuard.MarkGuest();
                    break;
                case SessionState.Closed:
                    Status = "Closed: " + Session.CloseReason;
                    if ((Session.CloseReason ?? "").Contains("version mismatch"))
                        CoopToast.Show(UpdateCheck.Newer != null ? UpdateCheck.Message()
                            : "Everyone needs the same co-op mod version: " + UpdateCheck.ReleasesPage);
                    SessionClosed?.Invoke(Session.CloseReason);
                    break;
            }
            CoopPlugin.Log.LogInfo("Co-op: " + Status);
        }

        private void Update()
        {
            if (!TestClientMode.Active) Steam.SteamLobby.EnsureInitialized();
            HandleAutoArgs();
            Menu.MainMenuCoop.Tick();
            Dungeon.EnemySync.Tick();
            Dungeon.EnemyMirror.Tick();
            Dungeon.ProjectileMirror.Tick();
            Dungeon.SummonSync.Tick();
            Sync.PartnerSounds.Tick();
            Dungeon.FloorItems.Tick();
            Dungeon.ChestSync.Tick();
            Dungeon.RoomObjectSync.Tick();
            Dungeon.LateApply.Tick();
            Shared.GoldSync.Tick();
            Shared.DaySync.Tick();
            Shared.WorldSync.Tick();
            Shared.ShopSync.Tick();
            Shared.TownSync.Tick();
            Shared.CitizenMirror.Tick();

            if (Session == null) return;
            try
            {
                Session.Tick();
            }
            catch (Exception e)
            {
                CoopPlugin.Log.LogError("Co-op session error: " + e);
                try { Session.Close("Error: " + e.Message); } catch (Exception) { }
            }
        }

        private void OnApplicationQuit() => Leave();
    }
}
