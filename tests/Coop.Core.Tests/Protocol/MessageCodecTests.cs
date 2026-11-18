using System.IO;
using Coop.Core.Protocol;
using Xunit;

namespace Coop.Core.Tests.Protocol
{
    public class MessageCodecTests
    {
        private static T RoundTrip<T>(MessageType type, T msg)
        {
            var (decodedType, decoded) = MessageCodec.Decode(MessageCodec.Encode(type, msg));
            Assert.Equal(type, decodedType);
            return Assert.IsType<T>(decoded);
        }

        [Fact]
        public void Hello_round_trips()
        {
            var h = RoundTrip(MessageType.Hello, new Hello { ModVersion = "0.1.0", GameVersion = "1.15.8", OwnedDlc = new[] { "Between Dimensions" } });
            Assert.Equal("0.1.0", h.ModVersion);
            Assert.Equal("1.15.8", h.GameVersion);
            Assert.Equal(new[] { "Between Dimensions" }, h.OwnedDlc);
        }

        [Fact]
        public void Hello_with_no_dlc_round_trips_as_empty_array()
        {
            var h = RoundTrip(MessageType.Hello, new Hello { ModVersion = "a", GameVersion = "b", OwnedDlc = new string[0] });
            Assert.Empty(h.OwnedDlc);
        }

        [Fact]
        public void Welcome_round_trips()
        {
            Assert.Equal(1, RoundTrip(MessageType.Welcome, new Welcome { PlayerIndex = 1 }).PlayerIndex);
        }

        [Fact]
        public void Reject_round_trips()
        {
            var r = RoundTrip(MessageType.Reject, new Reject { Reason = RejectReason.Dlc, Detail = "DLC mismatch" });
            Assert.Equal(RejectReason.Dlc, r.Reason);
            Assert.Equal("DLC mismatch", r.Detail);
        }

        [Fact]
        public void PlayerState_round_trips_with_layers()
        {
            var s = new PlayerState
            {
                Tick = 4242, SceneId = 7, X = 1.5f, Y = -2.25f, Z = 0.125f,
                Layers = new[]
                {
                    new SpriteLayer { Slot = 0, SpriteId = 300, Visible = true, FlipX = true, SortingOrder = -5, LocalX = 0.1f, LocalY = 0.2f },
                    new SpriteLayer { Slot = 1, SpriteId = 0, Visible = false, FlipX = false, SortingOrder = 12, LocalX = 0f, LocalY = -1f },
                    new SpriteLayer { Slot = 2, SpriteId = 65535, Visible = true, FlipX = false, SortingOrder = 0, LocalX = 3f, LocalY = 4f },
                }
            };
            var d = RoundTrip(MessageType.PlayerState, s);
            Assert.Equal(4242u, d.Tick);
            Assert.Equal(7, d.SceneId);
            Assert.Equal(1.5f, d.X);
            Assert.Equal(-2.25f, d.Y);
            Assert.Equal(0.125f, d.Z);
            Assert.Equal(3, d.Layers.Length);
            Assert.True(d.Layers[0].FlipX);
            Assert.Equal(-5, d.Layers[0].SortingOrder);
            Assert.Equal(300, d.Layers[0].SpriteId);
            Assert.False(d.Layers[1].Visible);
            Assert.Equal(-1f, d.Layers[1].LocalY);
            Assert.Equal(2, d.Layers[2].Slot);
            Assert.Equal(65535, d.Layers[2].SpriteId);
        }

        [Fact]
        public void Ping_and_Pong_round_trip()
        {
            Assert.Equal(99u, RoundTrip(MessageType.Ping, new Ping { Nonce = 99 }).Nonce);
            Assert.Equal(7u, RoundTrip(MessageType.Pong, new Ping { Nonce = 7 }).Nonce);
        }

        [Fact]
        public void Bye_round_trips_with_no_body()
        {
            var (type, msg) = MessageCodec.Decode(MessageCodec.Encode(MessageType.Bye, null));
            Assert.Equal(MessageType.Bye, type);
            Assert.Null(msg);
        }

        [Fact]
        public void Unknown_type_byte_throws()
        {
            Assert.Throws<InvalidDataException>(() => MessageCodec.Decode(new byte[] { 99 }));
        }

        [Fact]
        public void Empty_payload_throws()
        {
            Assert.Throws<InvalidDataException>(() => MessageCodec.Decode(new byte[0]));
        }

        [Fact]
        public void Truncated_body_throws_invalid_data()
        {
            var bytes = MessageCodec.Encode(MessageType.Hello, new Hello { ModVersion = "0.1.0", GameVersion = "1.15.8", OwnedDlc = new string[0] });
            var cut = new byte[bytes.Length - 3];
            System.Array.Copy(bytes, cut, cut.Length);
            Assert.Throws<InvalidDataException>(() => MessageCodec.Decode(cut));
        }
    }
}
