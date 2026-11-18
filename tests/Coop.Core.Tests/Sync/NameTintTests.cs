using Coop.Core.Sync;
using Xunit;

namespace Coop.Core.Tests.Sync
{
    public class NameTintTests
    {
        [Fact]
        public void Full_health_is_green()
        {
            var (r, g, b) = NameTint.For(100f, 100f, false);
            Assert.True(g > 0.8f && r < 0.5f);
        }

        [Fact]
        public void Half_health_is_yellow()
        {
            var (r, g, b) = NameTint.For(50f, 100f, false);
            Assert.True(r > 0.8f && g > 0.8f && b < 0.5f);
        }

        [Fact]
        public void Low_health_is_red()
        {
            var (r, g, b) = NameTint.For(5f, 100f, false);
            Assert.True(r > 0.8f && g < 0.35f);
        }

        [Fact]
        public void Ghost_is_grey()
        {
            var (r, g, b) = NameTint.For(0f, 100f, true);
            Assert.True(System.Math.Abs(r - g) < 0.01f && System.Math.Abs(g - b) < 0.01f);
        }

        [Fact]
        public void Unknown_health_is_white()
        {
            Assert.Equal((1f, 1f, 1f), NameTint.For(-1f, 0f, false));
        }
    }
}
