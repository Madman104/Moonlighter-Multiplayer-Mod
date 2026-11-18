using System;
using Coop.Plugin.Runtime;
using DigitalSunGames.Platforms;
using Steamworks;

namespace Coop.Plugin.Steam
{
    /// <summary>
    /// Friends-only Steam lobby used to find each other. The host creates it and invites a
    /// friend through the Steam overlay; the friend joins from the invite (game running) or
    /// from a launch with +connect_lobby (game was closed).
    /// </summary>
    internal sealed class SteamLobby
    {
        public const string VersionKey = "coop_mod_version";

        private static SteamLobby _instance;

        private readonly Callback<GameLobbyJoinRequested_t> _joinRequested;
        private readonly Callback<LobbyEnter_t> _lobbyEnter;
        private readonly Callback<LobbyChatUpdate_t> _chatUpdate;
        private readonly CallResult<LobbyCreated_t> _created = CallResult<LobbyCreated_t>.Create();

        private CSteamID _lobby = CSteamID.Nil;
        private SteamP2PTransport _transport;

        public static bool Available => SteamManager.Instance != null && SteamManager.Instance.Initialized;

        /// <summary>Hooks Steam callbacks once Steam is up. Safe to call every frame.</summary>
        public static void EnsureInitialized()
        {
            if (_instance != null || !Available) return;
            _instance = new SteamLobby();
            CoopPlugin.Log.LogInfo("Steam co-op ready");
            _instance.HandleLaunchArgs();
        }

        private SteamLobby()
        {
            _joinRequested = Callback<GameLobbyJoinRequested_t>.Create(r => Join(r.m_steamIDLobby));
            _lobbyEnter = Callback<LobbyEnter_t>.Create(OnLobbyEnter);
            _chatUpdate = Callback<LobbyChatUpdate_t>.Create(OnChatUpdate);
            CoopManager.Instance.SessionClosed += _ => LeaveLobby();
        }

        private void HandleLaunchArgs()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "+connect_lobby" && ulong.TryParse(args[i + 1], out var id))
                    Join(new CSteamID(id));
        }

        public static void HostNow() => _instance?.Host();

        public void Host()
        {
            CoopManager.Instance.Leave();
            LeaveLobby();
            var call = SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, 2);
            _created.Set(call, OnLobbyCreated);
            CoopPlugin.Log.LogInfo("Creating Steam lobby...");
        }

        private void OnLobbyCreated(LobbyCreated_t result, bool ioFailure)
        {
            if (ioFailure || result.m_eResult != EResult.k_EResultOK)
            {
                CoopPlugin.Log.LogWarning("Could not create Steam lobby: " + result.m_eResult);
                return;
            }
            _lobby = new CSteamID(result.m_ulSteamIDLobby);
            SteamMatchmaking.SetLobbyData(_lobby, VersionKey, CoopPlugin.ModVersion);
            CoopPlugin.Log.LogInfo("Lobby created " + result.m_ulSteamIDLobby);

            _transport = new SteamP2PTransport(isHost: true);
            CoopManager.Instance.StartWith(_transport, "Steam lobby open. Invite a friend from the Steam overlay.");
            SteamFriends.ActivateGameOverlayInviteDialog(_lobby);
        }

        private void Join(CSteamID lobby)
        {
            if (TestMode.TestClientMode.Active) return;
            CoopManager.Instance.Leave();
            LeaveLobby();
            CoopPlugin.Log.LogInfo("Joining Steam lobby " + lobby.m_SteamID);
            SteamMatchmaking.JoinLobby(lobby);
        }

        private void OnLobbyEnter(LobbyEnter_t e)
        {
            var lobby = new CSteamID(e.m_ulSteamIDLobby);
            var owner = SteamMatchmaking.GetLobbyOwner(lobby);
            if (owner == SteamUser.GetSteamID()) return; // the host entering its own lobby

            _lobby = lobby;
            var hostVersion = SteamMatchmaking.GetLobbyData(lobby, VersionKey);
            if (hostVersion != CoopPlugin.ModVersion)
                CoopPlugin.Log.LogWarning($"Host runs co-op mod {hostVersion}, you run {CoopPlugin.ModVersion}; the host will refuse the connection.");

            _transport = new SteamP2PTransport(isHost: false);
            CoopManager.Instance.StartWith(_transport, "Joined Steam lobby, connecting to host...");
            _transport.SetRemote(owner);
        }

        private void OnChatUpdate(LobbyChatUpdate_t u)
        {
            if (_transport == null || u.m_ulSteamIDLobby != _lobby.m_SteamID) return;
            var who = new CSteamID(u.m_ulSteamIDUserChanged);
            if (who == SteamUser.GetSteamID()) return;

            const uint entered = (uint)EChatMemberStateChange.k_EChatMemberStateChangeEntered;
            if ((u.m_rgfChatMemberStateChange & entered) != 0)
            {
                if (_transport.IsHost) _transport.SetRemote(who);
            }
            else if (who == _transport.Remote)
            {
                _transport.NotifyRemoteLeft("Other player left the Steam lobby");
            }
        }

        private void LeaveLobby()
        {
            if (_lobby != CSteamID.Nil) SteamMatchmaking.LeaveLobby(_lobby);
            _lobby = CSteamID.Nil;
            _transport = null;
        }
    }
}
