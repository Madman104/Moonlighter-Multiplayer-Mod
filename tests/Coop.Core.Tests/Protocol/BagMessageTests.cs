using Coop.Core.Protocol;
using Xunit;

namespace Coop.Core.Tests.Protocol
{
    public class BagMessageTests
    {
        [Fact]
        public void BagSnapshot_round_trips()
        {
            var (t, m) = MessageCodec.Decode(MessageCodec.Encode(MessageType.BagSnapshot, new BagSnapshot { Json = "[{\"name\":\"Golem Core\"}]" }));
            Assert.Equal(MessageType.BagSnapshot, t);
            Assert.Equal("[{\"name\":\"Golem Core\"}]", Assert.IsType<BagSnapshot>(m).Json);
        }
    }
}
