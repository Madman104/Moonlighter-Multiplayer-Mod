using System.Collections.Generic;
using Coop.Core.Protocol;
using Coop.Core.Session;
using Xunit;

namespace Coop.Core.Tests.Session
{
    public class PeerSessionTests
    {
        private double _now;
        private double Clock() => _now;

        private static Hello H(string mod = "0.1.0", params string[] dlc)
            => new Hello { ModVersion = mod, GameVersion = "1.15.8", OwnedDlc = dlc };

        private (PeerSession host, PeerSession client, FakeTransport ht, FakeTransport ct) Pair(Hello hostHello, Hello clientHello, bool ignoreDlc = false)
        {
            var (ht, ct) = FakeTransport.CreatePair();
            var host = new PeerSession(ht, hostHello, ignoreDlc, Clock);
            var client = new PeerSession(ct, clientHello, ignoreDlc, Clock);
            ht.ConnectBoth();
            return (host, client, ht, ct);
        }

        private static void Pump(PeerSession a, PeerSession b, int rounds = 5)
        {
            for (int i = 0; i < rounds; i++) { a.Tick(); b.Tick(); }
        }

        [Fact]
        public void Matching_peers_reach_connected()
        {
            var (host, client, _, _) = Pair(H(), H());
            Assert.Equal(SessionState.Idle, host.State);
            Pump(host, client);
            Assert.Equal(SessionState.Connected, host.State);
            Assert.Equal(SessionState.Connected, client.State);
            Assert.Equal(0, host.PlayerIndex);
            Assert.Equal(1, client.PlayerIndex);
        }

        [Fact]
        public void Mod_version_mismatch_closes_client_with_reason()
        {
            var (host, client, _, _) = Pair(H("0.1.0"), H("0.1.1"));
            Pump(host, client);
            Assert.Equal(SessionState.Closed, client.State);
            Assert.Contains("mod version", client.CloseReason);
            Assert.Equal(SessionState.Closed, host.State);
        }

        [Fact]
        public void Dlc_mismatch_connects_when_ignored()
        {
            var (host, client, _, _) = Pair(H(dlc: "Between Dimensions"), H(), ignoreDlc: true);
            Pump(host, client);
            Assert.Equal(SessionState.Connected, client.State);
        }

        [Fact]
        public void Host_honors_ignore_dlc_set_after_construction()
        {
            var (host, client, _, _) = Pair(H(dlc: "Between Dimensions"), H());
            host.IgnoreDlc = true;
            Pump(host, client);
            Assert.Equal(SessionState.Connected, client.State);
        }

        [Fact]
        public void Silence_for_over_ten_seconds_times_out()
        {
            var (host, client, _, _) = Pair(H(), H());
            Pump(host, client);
            _now = 10.5;
            client.Tick(); // host never ticks, so nothing reaches the client
            Assert.Equal(SessionState.Closed, client.State);
            Assert.Equal("Timed out", client.CloseReason);
        }

        [Fact]
        public void Pings_keep_a_quiet_session_alive()
        {
            var (host, client, _, _) = Pair(H(), H());
            Pump(host, client);
            for (int i = 0; i < 30; i++) { _now += 1.0; Pump(host, client, 2); }
            Assert.Equal(SessionState.Connected, host.State);
            Assert.Equal(SessionState.Connected, client.State);
        }

        [Fact]
        public void Handshake_that_never_answers_times_out()
        {
            var (host, client, _, _) = Pair(H(), H());
            client.Tick(); // client connects and sends Hello, host never answers
            _now = 10.5;
            client.Tick();
            Assert.Equal(SessionState.Closed, client.State);
            Assert.Equal("Timed out", client.CloseReason);
        }

        [Fact]
        public void Handshake_stuck_while_pings_flow_times_out()
        {
            var (host, client, _, ct) = Pair(H(), H());
            host.Tick();   // host: connected, waiting for Hello
            ct.DropNextSends = 1; // the client's Hello is lost
            client.Tick(); // client: connected, Hello "sent"
            for (int i = 0; i < 12; i++) { _now += 1.0; Pump(host, client, 2); }
            Assert.Equal(SessionState.Closed, client.State);
            Assert.Equal("Timed out", client.CloseReason);
            Assert.Equal(SessionState.Closed, host.State);
        }

        [Fact]
        public void Player_state_from_host_reaches_client_unreliably()
        {
            var (host, client, ht, _) = Pair(H(), H());
            Pump(host, client);
            var got = new List<PlayerState>();
            client.PlayerStateReceived += got.Add;
            host.SendPlayerState(new PlayerState { Tick = 77, SceneId = 3, Layers = new SpriteLayer[0] });
            Assert.False(ht.LastSendReliable);
            client.Tick();
            Assert.Single(got);
            Assert.Equal(77u, got[0].Tick);
        }

        [Fact]
        public void Sprite_def_reaches_peer_reliably()
        {
            var (host, client, _, ct) = Pair(H(), H());
            Pump(host, client);
            var got = new List<SpriteDef>();
            host.SpriteDefReceived += got.Add;
            client.SendSpriteDef(new SpriteDef { Id = 4, Key = "a/b" });
            Assert.True(ct.LastSendReliable);
            host.Tick();
            Assert.Single(got);
            Assert.Equal("a/b", got[0].Key);
        }

        [Fact]
        public void Player_state_is_not_sent_before_connected()
        {
            var (host, _, ht, _) = Pair(H(), H());
            int before = ht.SentCount;
            host.SendPlayerState(new PlayerState { Layers = new SpriteLayer[0] });
            Assert.Equal(before, ht.SentCount);
        }

        [Fact]
        public void Link_drop_closes_with_transport_reason()
        {
            var (host, client, ht, _) = Pair(H(), H());
            Pump(host, client);
            ht.DropLink("Disconnected");
            Pump(host, client, 1);
            Assert.Equal(SessionState.Closed, host.State);
            Assert.Equal("Disconnected", host.CloseReason);
        }

        [Fact]
        public void Leaving_sends_bye_and_peer_closes()
        {
            var (host, client, _, _) = Pair(H(), H());
            Pump(host, client);
            client.Close("Left");
            Assert.Equal(SessionState.Closed, client.State);
            host.Tick();
            Assert.Equal(SessionState.Closed, host.State);
            Assert.Equal("Other player left", host.CloseReason);
        }

        [Fact]
        public void Garbage_message_closes_with_protocol_error()
        {
            var (host, client, ht, _) = Pair(H(), H());
            Pump(host, client);
            ht.Send(new byte[] { 200 }, true);
            client.Tick();
            Assert.Equal(SessionState.Closed, client.State);
            Assert.StartsWith("Protocol error", client.CloseReason);
        }

        [Fact]
        public void State_changes_are_reported()
        {
            var (host, client, _, _) = Pair(H(), H());
            var seen = new List<SessionState>();
            client.StateChanged += seen.Add;
            Pump(host, client);
            Assert.Equal(new[] { SessionState.Handshaking, SessionState.Connected }, seen);
        }
    }
}
