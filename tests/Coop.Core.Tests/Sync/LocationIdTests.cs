using Coop.Core.Sync;
using Xunit;

namespace Coop.Core.Tests.Sync
{
    public class LocationIdTests
    {
        [Fact]
        public void Town_only_is_the_town_location()
        {
            Assert.Equal(LocationId.Town, LocationId.From(true, townLoaded: true, dungeonLoaded: false, 0, 0));
        }

        [Fact]
        public void Dungeon_wins_while_both_scenes_are_loaded()
        {
            Assert.Equal(100 + 10 + 0, LocationId.From(true, townLoaded: true, dungeonLoaded: true, 1, 0));
        }

        [Fact]
        public void None_is_never_visible()
        {
            Assert.True(LocationId.None < 0);
            Assert.True(LocationId.Town >= 0);
        }
    }
}
