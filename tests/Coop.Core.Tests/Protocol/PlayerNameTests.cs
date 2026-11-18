using Coop.Core.Protocol;
using Xunit;

namespace Coop.Core.Tests.Protocol
{
    public class PlayerNameTests
    {
        [Fact]
        public void PlayerName_round_trips()
        {
            var (t, m) = MessageCodec.Decode(MessageCodec.Encode(MessageType.PlayerName, new PlayerName { Name = "Wïll the Merchant" }));
            Assert.Equal(MessageType.PlayerName, t);
            Assert.Equal("Wïll the Merchant", Assert.IsType<PlayerName>(m).Name);
        }

        [Theory]
        [InlineData(null, "Player")]
        [InlineData("   ", "Player")]
        [InlineData("  Bob \n", "Bob")]
        [InlineData("abcdefghijklmnopqrstuvwxyz0123456789", "abcdefghijklmnopqrstuvwx")]
        public void Clean_trims_and_limits(string raw, string expected)
        {
            Assert.Equal(expected, PlayerName.Clean(raw));
        }
    }
}
