using Coop.Core.Protocol;
using Xunit;

namespace Coop.Core.Tests.Protocol
{
    public class EmblemMessageTests
    {
        [Fact]
        public void EmblemReturn_round_trips_without_payload()
        {
            var (t, m) = MessageCodec.Decode(MessageCodec.Encode(MessageType.EmblemReturn, null));
            Assert.Equal(MessageType.EmblemReturn, t);
            Assert.Null(m);
        }
    }
}
