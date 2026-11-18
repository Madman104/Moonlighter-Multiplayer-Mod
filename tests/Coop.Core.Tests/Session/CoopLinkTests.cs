using System.Collections.Generic;
using Coop.Core.Net;
using Coop.Core.Protocol;
using Coop.Core.Session;
using Xunit;

namespace Coop.Core.Tests.Session
{
    public class CoopLinkTests
    {
        private sealed class FakeListener : IGuestListener
        {
            public readonly Queue<ITransport> Pending = new Queue<ITransport>();
            public bool Disposed;
            public void Poll() { }
            public bool TryAccept(out ITransport transport)
            {
                if (Pending.Count > 0) { transport = Pending.Dequeue(); return true; }
                transport = null;
                return false;
            }
            public void Dispose() { Disposed = true; }
        }

        private static Hello Hello() => new Hello { ModVersion = "1", GameVersion = "g", OwnedDlc = new string[0] };

        private double _now;
        private FakeListener _listener;
        private CoopLink _host;

        private CoopLink NewHost()
        {
            _listener = new FakeListener();
            _host = CoopLink.Host(_listener, Hello(), false, () => _now);
            return _host;
        }

        private CoopLink AddGuest()
        {
            var (h, c) = FakeTransport.CreatePair();
            _listener.Pending.Enqueue(h);
            var guest = CoopLink.Guest(c, Hello(), false, () => _now);
            h.ConnectBoth();
            return guest;
        }

        private void Pump(params CoopLink[] links)
        {
            for (int i = 0; i < 6; i++)
            {
                _host.Tick();
                foreach (var l in links) l.Tick();
            }
        }

        [Fact]
        public void Guests_get_player_numbers_1_and_2()
        {
            NewHost();
            var a = AddGuest();
            var b = AddGuest();
            Pump(a, b);
            Assert.Equal(SessionState.Connected, _host.State);
            Assert.Equal(1, a.LocalIndex);
            Assert.Equal(2, b.LocalIndex);
            Assert.Equal(0, _host.LocalIndex);
            Assert.Equal(2, _host.PeerCount);
        }

        [Fact]
        public void A_guests_state_reaches_host_and_other_guest_with_its_number()
        {
            NewHost();
            var a = AddGuest();
            var b = AddGuest();
            Pump(a, b);
            int hostFrom = -1, bFrom = -1;
            _host.PlayerStateReceived += _ => hostFrom = _host.LastFrom;
            b.PlayerStateReceived += _ => bFrom = b.LastFrom;
            a.SendPlayerState(new PlayerState { Tick = 1, SceneId = 1, Layers = new SpriteLayer[0] });
            Pump(a, b);
            Assert.Equal(1, hostFrom);
            Assert.Equal(1, bFrom);
        }

        [Fact]
        public void Host_messages_reach_every_guest_from_player_0()
        {
            NewHost();
            var a = AddGuest();
            var b = AddGuest();
            Pump(a, b);
            var got = new List<(int who, int from)>();
            a.GameMessageReceived += (t, m) => { if (t == MessageType.GoldTotal) got.Add((1, a.LastFrom)); };
            b.GameMessageReceived += (t, m) => { if (t == MessageType.GoldTotal) got.Add((2, b.LastFrom)); };
            _host.SendGame(MessageType.GoldTotal, new GoldTotal { Value = 5 });
            Pump(a, b);
            Assert.Contains((1, 0), got);
            Assert.Contains((2, 0), got);
        }

        [Fact]
        public void SendGameTo_reaches_only_that_guest()
        {
            NewHost();
            var a = AddGuest();
            var b = AddGuest();
            Pump(a, b);
            int aGot = 0, bGot = 0;
            a.GameMessageReceived += (t, m) => { if (t == MessageType.GoldTotal) aGot++; };
            b.GameMessageReceived += (t, m) => { if (t == MessageType.GoldTotal) bGot++; };
            _host.SendGameTo(2, MessageType.GoldTotal, new GoldTotal { Value = 5 });
            Pump(a, b);
            Assert.Equal(0, aGot);
            Assert.Equal(1, bGot);
        }

        [Fact]
        public void Guest_game_messages_are_relayed_but_host_only_ones_are_not()
        {
            NewHost();
            var a = AddGuest();
            var b = AddGuest();
            Pump(a, b);
            var bTypes = new List<MessageType>();
            b.GameMessageReceived += (t, m) => bTypes.Add(t);
            int hostCatchUps = 0;
            _host.GameMessageReceived += (t, m) => { if (t == MessageType.CatchUp) hostCatchUps++; };
            a.SendGame(MessageType.GoldDelta, new GoldDelta { Delta = -10 });
            a.SendGame(MessageType.CatchUp, null);
            Pump(a, b);
            Assert.Contains(MessageType.GoldDelta, bTypes);
            Assert.DoesNotContain(MessageType.CatchUp, bTypes);
            Assert.Equal(1, hostCatchUps);
        }

        [Fact]
        public void A_fourth_guest_is_turned_away()
        {
            NewHost();
            var a = AddGuest();
            var b = AddGuest();
            var c = AddGuest();
            var (h, d) = FakeTransport.CreatePair();
            _listener.Pending.Enqueue(h);
            var fourth = CoopLink.Guest(d, Hello(), false, () => _now);
            h.ConnectBoth();
            Pump(a, b, c, fourth);
            Assert.Equal(3, _host.PeerCount);
            Assert.True(h.Disposed);
            Assert.NotEqual(SessionState.Connected, fourth.State);
        }

        [Fact]
        public void A_guest_leaving_frees_its_number()
        {
            NewHost();
            var a = AddGuest();
            var b = AddGuest();
            Pump(a, b);
            int left = -1;
            _host.PeerLeft += i => left = i;
            a.Close("bye");
            Pump(a, b);
            Assert.Equal(1, left);
            Assert.Equal(1, _host.PeerCount);
            var c = AddGuest();
            Pump(b, c);
            Assert.Equal(1, c.LocalIndex);
        }

        [Fact]
        public void A_late_guest_learns_the_names_of_guests_already_there()
        {
            NewHost();
            var a = AddGuest();
            Pump(a);
            a.SendGame(MessageType.PlayerName, new PlayerName { Name = "Alice" });
            Pump(a);
            var b = AddGuest();
            var names = new List<(int from, string name)>();
            b.GameMessageReceived += (t, m) => { if (t == MessageType.PlayerName) names.Add((b.LastFrom, ((PlayerName)m).Name)); };
            Pump(a, b);
            Assert.Contains((1, "Alice"), names);
        }

        [Fact]
        public void Host_stays_open_after_the_last_guest_leaves()
        {
            NewHost();
            var a = AddGuest();
            Pump(a);
            a.Close("bye");
            Pump(a);
            Assert.Equal(0, _host.PeerCount);
            Assert.NotEqual(SessionState.Closed, _host.State);
            Assert.NotEqual(SessionState.Connected, _host.State);
        }
    }
}
