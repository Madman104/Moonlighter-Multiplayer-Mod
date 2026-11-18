using Coop.Core.Protocol;
using Xunit;

namespace Coop.Core.Tests.Protocol
{
    public class FloorItemMessageTests
    {
        [Fact]
        public void FloorItemSpawn_round_trips()
        {
            var (t, m) = MessageCodec.Decode(MessageCodec.Encode(MessageType.FloorItemSpawn,
                new FloorItemSpawn { Id = (2 << 24) | 7, RoomKey = 12345, X = -3.5f, Y = 80f, Json = "{\"name\":\"Rich Jelly\"}" }));
            Assert.Equal(MessageType.FloorItemSpawn, t);
            var s = Assert.IsType<FloorItemSpawn>(m);
            Assert.Equal((2 << 24) | 7, s.Id);
            Assert.Equal(12345, s.RoomKey);
            Assert.Equal(-3.5f, s.X);
            Assert.Equal(80f, s.Y);
            Assert.Equal("{\"name\":\"Rich Jelly\"}", s.Json);
        }

        [Fact]
        public void FloorItemTaken_round_trips()
        {
            var (t, m) = MessageCodec.Decode(MessageCodec.Encode(MessageType.FloorItemTaken, new FloorItemTaken { Id = 9, Remaining = 3 }));
            Assert.Equal(MessageType.FloorItemTaken, t);
            var s = Assert.IsType<FloorItemTaken>(m);
            Assert.Equal(9, s.Id);
            Assert.Equal(3, s.Remaining);
        }
    }
}
