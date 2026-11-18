using System;
using Coop.Core.Protocol;
using Coop.Core.Session;
using Coop.Plugin.TestMode;

namespace Coop.Plugin.Runtime
{
    /// <summary>Both players' display names: the Steam name, sent to the partner on connect.</summary>
    internal static class PlayerNames
    {
        /// <summary>The partner's name; "P1"/"P2" until it arrives.</summary>
        public static string Partner { get; private set; } = "P2";

        public static string Local()
        {
            try
            {
                if (!TestClientMode.Active && Steam.SteamLobby.Available)
                    return PlayerName.Clean(Steamworks.SteamFriends.GetPersonaName());
            }
            catch (Exception) { /* fall through */ }
            return TestClientMode.Active ? "Test client" : "Player";
        }

        public static void Attach(PeerSession session)
        {
            Partner = session.IsHost ? "P2" : "P1";
            session.StateChanged += st =>
            {
                if (st == SessionState.Connected) session.SendGame(MessageType.PlayerName, new PlayerName { Name = Local() });
            };
            session.GameMessageReceived += (type, msg) =>
            {
                if (type == MessageType.PlayerName) Partner = PlayerName.Clean(((PlayerName)msg).Name);
            };
        }
    }
}
