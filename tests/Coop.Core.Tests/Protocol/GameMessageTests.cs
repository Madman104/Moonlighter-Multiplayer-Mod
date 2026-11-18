using System.Collections.Generic;
using Coop.Core.Protocol;
using Coop.Core.Session;
using Coop.Core.Tests.Session;
using Xunit;

namespace Coop.Core.Tests.Protocol
{
    public class GameMessageTests
    {
        private static T RoundTrip<T>(MessageType type, T msg)
        {
            var (decodedType, decoded) = MessageCodec.Decode(MessageCodec.Encode(type, msg));
            Assert.Equal(type, decodedType);
            return Assert.IsType<T>(decoded);
        }

        [Fact]
        public void FloorLayout_round_trips()
        {
            var d = RoundTrip(MessageType.FloorLayout, new FloorLayout { Culture = 3, Floor = 2, Json = "{\"rooms\":[]}" });
            Assert.Equal(3, d.Culture);
            Assert.Equal(2, d.Floor);
            Assert.Equal("{\"rooms\":[]}", d.Json);
        }

        [Fact]
        public void Large_FloorLayout_survives_codec_and_framing()
        {
            var json = new string('x', 200_000);
            var payload = MessageCodec.Encode(MessageType.FloorLayout, new FloorLayout { Culture = 1, Floor = 0, Json = json });
            var buf = new FrameBuffer();
            var framed = FrameBuffer.Frame(payload);
            buf.Append(framed, 0, framed.Length);
            Assert.True(buf.TryReadFrame(out var back));
            var (_, msg) = MessageCodec.Decode(back);
            Assert.Equal(200_000, ((FloorLayout)msg).Json.Length);
        }

        [Fact]
        public void DungeonTravel_round_trips()
        {
            var d = RoundTrip(MessageType.DungeonTravel, new DungeonTravel { Culture = 4, Level = 1, Replay = true });
            Assert.Equal(4, d.Culture);
            Assert.Equal(1, d.Level);
            Assert.True(d.Replay);
        }

        [Fact]
        public void TownTravel_round_trips()
        {
            Assert.True(RoundTrip(MessageType.TownTravel, new TownTravel { FromEmblem = true }).FromEmblem);
        }

        [Fact]
        public void RoomChange_round_trips_with_negative_coordinates()
        {
            var d = RoundTrip(MessageType.RoomChange, new RoomChange
            {
                FromX = -2, FromY = 5, ToX = -1, ToY = 5, DoorIndex = -1, Special = true, TeleportToLastDoor = true,
            });
            Assert.Equal(-2, d.FromX);
            Assert.Equal(5, d.FromY);
            Assert.Equal(-1, d.ToX);
            Assert.Equal(-1, d.DoorIndex);
            Assert.True(d.Special);
            Assert.True(d.TeleportToLastDoor);
        }

        [Fact]
        public void RoomChange_carries_secret_room_flags()
        {
            // A secret room shares its parent's grid position, so the flag tells them apart.
            var d = RoundTrip(MessageType.RoomChange, new RoomChange { ToX = 1, ToY = 1, FromSecret = true, ToSecret = false });
            Assert.True(d.FromSecret);
            Assert.False(d.ToSecret);
            d = RoundTrip(MessageType.RoomChange, new RoomChange { ToSecret = true });
            Assert.True(d.ToSecret);
            Assert.False(d.FromSecret);
            Assert.False(d.Special);
        }

        [Fact]
        public void Game_messages_reach_peer_only_when_connected()
        {
            double now = 0;
            var (ht, ct) = FakeTransport.CreatePair();
            var hello = new Hello { ModVersion = "1", GameVersion = "1", OwnedDlc = new string[0] };
            var host = new PeerSession(ht, hello, false, () => now);
            var client = new PeerSession(ct, hello, false, () => now);
            var got = new List<(MessageType, object)>();
            client.GameMessageReceived += (t, m) => got.Add((t, m));

            int before = ht.SentCount;
            host.SendGame(MessageType.TownTravel, new TownTravel());
            Assert.Equal(before, ht.SentCount); // not connected yet: dropped

            ht.ConnectBoth();
            for (int i = 0; i < 5; i++) { host.Tick(); client.Tick(); }
            host.SendGame(MessageType.RoomChange, new RoomChange { ToX = 1, ToY = 2 });
            Assert.True(ht.LastSendReliable);
            client.Tick();
            Assert.Single(got);
            Assert.Equal(MessageType.RoomChange, got[0].Item1);
            Assert.Equal(2, ((RoomChange)got[0].Item2).ToY);
        }
    }
}
