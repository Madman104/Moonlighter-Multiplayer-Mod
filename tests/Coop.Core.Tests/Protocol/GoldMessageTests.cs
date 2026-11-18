using Coop.Core.Protocol;
using Xunit;

namespace Coop.Core.Tests.Protocol
{
    public class GoldMessageTests
    {
        private static T RoundTrip<T>(MessageType type, T msg)
        {
            var (t, m) = MessageCodec.Decode(MessageCodec.Encode(type, msg));
            Assert.Equal(type, t);
            return Assert.IsType<T>(m);
        }

        [Fact]
        public void GoldDelta_round_trips_negative_spend()
        {
            Assert.Equal(-1250, RoundTrip(MessageType.GoldDelta, new GoldDelta { Delta = -1250 }).Delta);
        }

        [Fact]
        public void GoldTotal_round_trips()
        {
            Assert.Equal(98765, RoundTrip(MessageType.GoldTotal, new GoldTotal { Value = 98765 }).Value);
        }
    }
}
