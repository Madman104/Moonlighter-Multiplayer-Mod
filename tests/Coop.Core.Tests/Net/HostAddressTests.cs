using Coop.Core.Net;
using Xunit;

namespace Coop.Core.Tests.Net
{
    public class HostAddressTests
    {
        [Theory]
        [InlineData("127.0.0.1", "127.0.0.1")]
        [InlineData("  86.12.4.200 \r\n", "86.12.4.200")]
        [InlineData("my-pc.example.com", "my-pc.example.com")]
        [InlineData("[::1]", null)]
        public void Normalizes_valid_addresses(string input, string expected)
        {
            Assert.Equal(expected, HostAddress.TryNormalize(input, out var addr) ? addr : null);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("hello world")]
        [InlineData("1.2.3.4:27460")]
        [InlineData("some long text that someone copied by mistake from a chat window and is clearly not an address")]
        [InlineData("http://1.2.3.4")]
        public void Rejects_clipboard_junk(string input)
        {
            Assert.False(HostAddress.TryNormalize(input, out _));
        }
    }
}
