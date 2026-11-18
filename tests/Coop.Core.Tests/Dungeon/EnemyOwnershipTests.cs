using Coop.Core.Dungeon;
using Xunit;

namespace Coop.Core.Tests.Dungeon
{
    public class EnemyOwnershipTests
    {
        [Fact]
        public void Closer_guest_takes_over_only_past_the_margin()
        {
            // host at 0, guest at 100, enemy at 55: guest is 10 closer, margin 24 -> host keeps it
            Assert.False(EnemyOwnership.GuestShouldOwn(false, 55f * 55f, 45f * 45f, 24f));
            // enemy at 80: guest 20 away, host 80 away -> guest takes it
            Assert.True(EnemyOwnership.GuestShouldOwn(false, 80f * 80f, 20f * 20f, 24f));
        }

        [Fact]
        public void Guest_keeps_it_until_host_is_clearly_closer()
        {
            Assert.True(EnemyOwnership.GuestShouldOwn(true, 45f * 45f, 55f * 55f, 24f));
            Assert.False(EnemyOwnership.GuestShouldOwn(true, 10f * 10f, 60f * 60f, 24f));
        }

        [Fact]
        public void Unknown_guest_position_means_host_owns()
        {
            Assert.False(EnemyOwnership.GuestShouldOwn(true, 100f, float.PositiveInfinity, 24f));
        }

        [Fact]
        public void Room_key_is_unique_per_room_and_secret_flag()
        {
            Assert.NotEqual(EnemyOwnership.RoomKey(1, 2, false), EnemyOwnership.RoomKey(2, 1, false));
            Assert.NotEqual(EnemyOwnership.RoomKey(1, 2, false), EnemyOwnership.RoomKey(1, 2, true));
            Assert.NotEqual(EnemyOwnership.RoomKey(-1, 0, false), EnemyOwnership.RoomKey(0, -1, false));
        }

        [Fact]
        public void Nearest_of_four_takes_over_past_the_margin()
        {
            // current owner 0 at 100, player 2 at 10 -> 2 takes it
            var d = new[] { 100f * 100f, 80f * 80f, 10f * 10f, float.PositiveInfinity };
            Assert.Equal(2, EnemyOwnership.Owner(0, d, 24f));
            // current owner 1 at 40, player 2 at 30 (only 10 closer) -> 1 keeps it
            d = new[] { 100f * 100f, 40f * 40f, 30f * 30f, float.PositiveInfinity };
            Assert.Equal(1, EnemyOwnership.Owner(1, d, 24f));
        }

        [Fact]
        public void Owner_who_left_is_replaced_by_the_nearest()
        {
            var d = new[] { 50f * 50f, float.PositiveInfinity, 20f * 20f, 90f * 90f };
            Assert.Equal(2, EnemyOwnership.Owner(1, d, 24f));
        }

        [Fact]
        public void Nobody_in_the_room_means_the_host_runs_it()
        {
            var d = new[] { float.PositiveInfinity, float.PositiveInfinity };
            Assert.Equal(0, EnemyOwnership.Owner(1, d, 24f));
        }
    }
}
