using Coop.Core.Protocol;
using Xunit;

namespace Coop.Core.Tests.Protocol
{
    public class WorldMessageTests
    {
        [Fact]
        public void WorldSlot_round_trips_bytes()
        {
            var data = new byte[70000];
            for (int i = 0; i < data.Length; i++) data[i] = (byte)(i * 7);
            var (t, m) = MessageCodec.Decode(MessageCodec.Encode(MessageType.WorldSlot, new WorldSlot { Data = data }));
            Assert.Equal(MessageType.WorldSlot, t);
            Assert.Equal(data, Assert.IsType<WorldSlot>(m).Data);
        }

        [Fact]
        public void WorldSlot_text_survives_compression()
        {
            string json = "{\"seed\":12345,\"items\":[" + string.Join(",", new string[500]) + "]}";
            Assert.Equal(json, WorldSlot.Unpack(WorldSlot.Pack(json)));
            Assert.True(WorldSlot.Pack(json).Length < json.Length);
        }
    }
}
