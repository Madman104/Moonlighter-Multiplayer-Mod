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
    }
}
