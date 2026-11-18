using Coop.Core.Sync;
using Xunit;

namespace Coop.Core.Tests.Sync
{
    public class LocationIdTests
    {
        [Fact]
        public void Town_only_is_the_town_location()
        {
            Assert.Equal(LocationId.Town, LocationId.From(hasWill: true, townLoaded: true, dungeonLoaded: false));
        }

        [Fact]
        public void Any_dungeon_is_not_shared_yet()
        {
            Assert.Equal(LocationId.None, LocationId.From(true, townLoaded: false, dungeonLoaded: true));
            Assert.Equal(LocationId.None, LocationId.From(true, townLoaded: true, dungeonLoaded: true));
        }

        [Fact]
        public void No_will_or_nothing_loaded_is_none()
        {
            Assert.Equal(LocationId.None, LocationId.From(false, true, false));
            Assert.Equal(LocationId.None, LocationId.From(true, false, false));
        }

        [Fact]
        public void None_is_never_visible()
        {
            Assert.True(LocationId.None < 0);
            Assert.True(LocationId.Town >= 0);
        }
    }
}
