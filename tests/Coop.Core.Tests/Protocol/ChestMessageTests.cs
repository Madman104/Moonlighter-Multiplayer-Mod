using Coop.Core.Protocol;
using Xunit;

namespace Coop.Core.Tests.Protocol
{
    public class ChestMessageTests
    {
        [Fact]
        public void ChestUse_round_trips()
        {
            var (t, m) = MessageCodec.Decode(MessageCodec.Encode(MessageType.ChestUse, new ChestUse { RoomKey = 77, Index = 2, Open = true }));
            Assert.Equal(MessageType.ChestUse, t);
            var c = Assert.IsType<ChestUse>(m);
            Assert.Equal(77, c.RoomKey);
            Assert.Equal(2, c.Index);
            Assert.True(c.Open);
        }

        [Fact]
        public void ChestContents_round_trips()
        {
            var (t, m) = MessageCodec.Decode(MessageCodec.Encode(MessageType.ChestContents, new ChestContents { RoomKey = -5, Index = 1, Json = "{\"items\":[]}" }));
            Assert.Equal(MessageType.ChestContents, t);
            var c = Assert.IsType<ChestContents>(m);
            Assert.Equal(-5, c.RoomKey);
            Assert.Equal(1, c.Index);
            Assert.Equal("{\"items\":[]}", c.Json);
        }

        [Fact]
        public void RoomObject_round_trips()
        {
            var (t, m) = MessageCodec.Decode(MessageCodec.Encode(MessageType.RoomObject,
                new RoomObject { RoomKey = 3, Kind = RoomObject.Pool, X = 1f, Y = 2f, Value = 0.25f }));
            Assert.Equal(MessageType.RoomObject, t);
            var r = Assert.IsType<RoomObject>(m);
            Assert.Equal(3, r.RoomKey);
            Assert.Equal(RoomObject.Pool, r.Kind);
            Assert.Equal(1f, r.X);
            Assert.Equal(2f, r.Y);
            Assert.Equal(0.25f, r.Value);
        }

        [Fact]
        public void BreakableBroken_round_trips()
        {
            var (t, m) = MessageCodec.Decode(MessageCodec.Encode(MessageType.BreakableBroken, new BreakableBroken { RoomKey = 9, X = 12.5f, Y = -40f }));
            Assert.Equal(MessageType.BreakableBroken, t);
            var b = Assert.IsType<BreakableBroken>(m);
            Assert.Equal(9, b.RoomKey);
            Assert.Equal(12.5f, b.X);
            Assert.Equal(-40f, b.Y);
        }
    }
}
