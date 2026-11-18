using Coop.Core.Protocol;
using Xunit;

namespace Coop.Core.Tests.Protocol
{
    public class DayTimeMessageTests
    {
        [Fact]
        public void DayTimeSync_round_trips()
        {
            var (t, m) = MessageCodec.Decode(MessageCodec.Encode(MessageType.DayTimeSync, new DayTimeSync { Value = 1, FromBed = true }));
            Assert.Equal(MessageType.DayTimeSync, t);
            var d = Assert.IsType<DayTimeSync>(m);
            Assert.Equal(1, d.Value);
            Assert.True(d.FromBed);
        }
    }
}
