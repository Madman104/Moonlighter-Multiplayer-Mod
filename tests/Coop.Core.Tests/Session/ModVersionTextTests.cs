using Coop.Core.Session;
using Xunit;

namespace Coop.Core.Tests.Session
{
    public class ModVersionTextTests
    {
        [Theory]
        [InlineData("v1.6.0", "1.5.0", true)]
        [InlineData("1.5.1", "1.5.0", true)]
        [InlineData("v1.10.0", "1.9.9", true)]
        [InlineData("v1.5.0", "1.5.0", false)]
        [InlineData("v1.4.0", "1.5.0", false)]
        [InlineData("nonsense", "1.5.0", false)]
        [InlineData(null, "1.5.0", false)]
        public void IsNewer_compares_numbers(string candidate, string current, bool expected)
        {
            Assert.Equal(expected, ModVersionText.IsNewer(candidate, current));
        }

        [Fact]
        public void Tag_is_read_from_release_json()
        {
            Assert.Equal("v1.6.0", ModVersionText.TagFromReleaseJson("{\"url\":\"x\",\"tag_name\": \"v1.6.0\",\"name\":\"y\"}"));
            Assert.Null(ModVersionText.TagFromReleaseJson("{\"message\":\"Not Found\"}"));
        }
    }
}
