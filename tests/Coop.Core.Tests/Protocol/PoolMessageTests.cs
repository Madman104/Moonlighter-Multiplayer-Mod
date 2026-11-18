using Coop.Core.Protocol;
using Xunit;

namespace Coop.Core.Tests.Protocol
{
    public class PoolMessageTests
    {
        [Fact]
        public void PoolSpent_round_trips()
        {
            var (t, m) = MessageCodec.Decode(MessageCodec.Encode(MessageType.PoolSpent, new PoolSpent { X = -2, Y = 7, Secret = true }));
            Assert.Equal(MessageType.PoolSpent, t);
            var p = Assert.IsType<PoolSpent>(m);
            Assert.Equal(-2, p.X);
            Assert.Equal(7, p.Y);
            Assert.True(p.Secret);
        }
    }
}
