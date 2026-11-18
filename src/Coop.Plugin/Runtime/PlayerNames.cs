using System;
using System.Collections.Generic;
using Coop.Core.Protocol;
using Coop.Core.Session;
using Coop.Plugin.TestMode;

namespace Coop.Plugin.Runtime
{
    /// <summary>Every player's display name (Steam name), by player number (host 0, guests 1..3).</summary>
    internal static class PlayerNames
    {
        private static readonly Dictionary<int, string> Names = new Dictionary<int, string>();

        /// <summary>A player's name; "P1".."P4" until it arrives.</summary>
        public static string Name(int player)
            => Names.TryGetValue(player, out var n) ? n : "P" + (player + 1);

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

        public static void Attach(CoopLink link)
        {
            Names.Clear();
            // Guest: tell everyone once connected (the host forwards it). Host: tell each newcomer.
            link.StateChanged += st =>
            {
                if (st == SessionState.Connected && !link.IsHost) link.SendGame(MessageType.PlayerName, new PlayerName { Name = Local() });
            };
            link.PeerJoined += i =>
            {
                if (link.IsHost) link.SendGameTo(i, MessageType.PlayerName, new PlayerName { Name = Local() });
            };
            link.PeerLeft += i => Names.Remove(i);
            link.GameMessageReceived += (type, msg) =>
            {
                if (type == MessageType.PlayerName) Names[link.LastFrom] = PlayerName.Clean(((PlayerName)msg).Name);
            };
        }
    }
}
