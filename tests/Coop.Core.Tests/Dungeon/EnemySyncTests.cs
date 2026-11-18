using Coop.Core.Dungeon;
using Coop.Core.Protocol;
using Xunit;

namespace Coop.Core.Tests.Dungeon
{
    public class EnemySyncTests
    {
        private static T RoundTrip<T>(MessageType type, T msg)
        {
            var (t, m) = MessageCodec.Decode(MessageCodec.Encode(type, msg));
            Assert.Equal(type, t);
            return Assert.IsType<T>(m);
        }

        [Fact]
        public void EnemyDamage_round_trips()
        {
            var d = RoundTrip(MessageType.EnemyDamage, new EnemyDamage { X = -3, Y = 4, Secret = true, Index = 7, Delta = 12.5f });
            Assert.Equal(-3, d.X);
            Assert.Equal(4, d.Y);
            Assert.True(d.Secret);
            Assert.Equal(7, d.Index);
            Assert.Equal(12.5f, d.Delta);
        }

        [Fact]
        public void EnemyHealth_round_trips()
        {
            var h = RoundTrip(MessageType.EnemyHealth, new EnemyHealth { X = 1, Y = 2, Secret = false, Hp = new[] { 10f, 0f, 33.25f } });
            Assert.Equal(new[] { 10f, 0f, 33.25f }, h.Hp);
            Assert.Equal(2, h.Y);
        }

        [Fact]
        public void Room_seed_is_stable_and_differs_per_room()
        {
            Assert.Equal(RoomSeed.For(1, 0, 2, 3, false), RoomSeed.For(1, 0, 2, 3, false));
            Assert.NotEqual(RoomSeed.For(1, 0, 2, 3, false), RoomSeed.For(1, 0, 3, 2, false));
            Assert.NotEqual(RoomSeed.For(1, 0, 2, 3, false), RoomSeed.For(1, 0, 2, 3, true));
            Assert.NotEqual(RoomSeed.For(1, 0, 2, 3, false), RoomSeed.For(1, 1, 2, 3, false));
            Assert.NotEqual(RoomSeed.For(1, 0, 2, 3, false), RoomSeed.For(2, 0, 2, 3, false));
        }

        [Theory]
        [InlineData(100f, 100f, false)]
        [InlineData(100f, 100.4f, false)]
        [InlineData(100f, 99f, true)]
        [InlineData(0f, 5f, true)]
        public void Health_correction_ignores_tiny_drift(float local, float host, bool expected)
        {
            Assert.Equal(expected, EnemyHealthRules.NeedsCorrection(local, host));
        }

        [Fact]
        public void Coop_health_factor_is_two()
        {
            Assert.Equal(2f, EnemyHealthRules.CoopMaxHealthFactor);
        }
    }
}
