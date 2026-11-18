using Coop.Core.Dungeon;
using Coop.Core.Sync;
using Xunit;

namespace Coop.Core.Tests.Dungeon
{
    public class FloorLayoutStoreTests
    {
        [Fact]
        public void Take_returns_matching_floor_once()
        {
            var s = new FloorLayoutStore();
            s.Put(1, 0, "a");
            Assert.True(s.TryTake(1, 0, out var json));
            Assert.Equal("a", json);
            Assert.False(s.TryTake(1, 0, out _));
        }

        [Fact]
        public void Other_floor_is_not_returned()
        {
            var s = new FloorLayoutStore();
            s.Put(1, 0, "a");
            Assert.False(s.TryTake(1, 1, out _));
            Assert.False(s.TryTake(2, 0, out _));
        }

        [Fact]
        public void Newer_layout_for_same_floor_replaces_older()
        {
            var s = new FloorLayoutStore();
            s.Put(1, 0, "old");
            s.Put(1, 0, "new");
            Assert.True(s.TryTake(1, 0, out var json));
            Assert.Equal("new", json);
        }

        [Fact]
        public void Clear_drops_everything()
        {
            var s = new FloorLayoutStore();
            s.Put(1, 0, "a");
            s.Clear();
            Assert.False(s.TryTake(1, 0, out _));
        }
    }

    public class TravelArbiterTests
    {
        [Fact]
        public void Client_always_applies_remote_travel()
        {
            var a = new TravelArbiter(isHost: false);
            a.LocalTravel(1.0);
            Assert.True(a.ShouldApplyRemote(1.1));
        }

        [Fact]
        public void Host_ignores_remote_travel_right_after_its_own()
        {
            var a = new TravelArbiter(isHost: true, window: 0.5);
            a.LocalTravel(1.0);
            Assert.False(a.ShouldApplyRemote(1.3));
        }

        [Fact]
        public void Host_applies_remote_travel_after_the_window()
        {
            var a = new TravelArbiter(isHost: true, window: 0.5);
            a.LocalTravel(1.0);
            Assert.True(a.ShouldApplyRemote(1.6));
        }

        [Fact]
        public void Host_with_no_local_travel_applies_remote()
        {
            Assert.True(new TravelArbiter(isHost: true).ShouldApplyRemote(0.0));
        }
    }

    public class DungeonLocationTests
    {
        [Fact]
        public void Dungeon_location_encodes_culture_and_floor()
        {
            Assert.Equal(100 + 3 * 10 + 2, LocationId.From(true, false, true, culture: 3, floor: 2));
        }

        [Fact]
        public void Different_floors_are_different_locations()
        {
            Assert.NotEqual(LocationId.From(true, false, true, 1, 0), LocationId.From(true, false, true, 1, 1));
        }

        [Fact]
        public void Town_still_town_and_nothing_still_none()
        {
            Assert.Equal(LocationId.Town, LocationId.From(true, true, false, 0, 0));
            Assert.Equal(LocationId.None, LocationId.From(false, false, true, 1, 0));
            Assert.Equal(LocationId.None, LocationId.From(true, false, false, 0, 0));
        }

        [Fact]
        public void Unknown_dungeon_culture_is_none()
        {
            Assert.Equal(LocationId.None, LocationId.From(true, false, true, culture: -1, floor: 0));
        }
    }
}
