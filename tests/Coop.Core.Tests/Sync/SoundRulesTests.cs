using Coop.Core.Protocol;
using Coop.Core.Sync;
using Xunit;

namespace Coop.Core.Tests.Sync
{
    public class SoundRulesTests
    {
        [Theory]
        [InlineData(0f, 1f)]
        [InlineData(10f, 1f)]
        [InlineData(20f, 0.5f)]
        [InlineData(30f, 0f)]
        [InlineData(99f, 0f)]
        public void Falloff_is_full_then_linear_then_silent(float distance, float expected)
        {
            Assert.Equal(expected, SoundFalloff.Volume(distance, 10f, 30f), 3);
        }

        [Fact]
        public void Falloff_nan_is_silent()
        {
            Assert.Equal(0f, SoundFalloff.Volume(float.NaN, 10f, 30f));
        }

        [Fact]
        public void Throttle_limits_repeats_of_one_sound()
        {
            var t = new SoundThrottle();
            Assert.True(t.Allow("step", 0f));
            Assert.False(t.Allow("step", 0.01f));
            Assert.True(t.Allow("swing", 0.01f));
            Assert.True(t.Allow("step", 0.06f));
        }

        [Fact]
        public void Throttle_caps_sounds_per_second_but_never_stops()
        {
            var t = new SoundThrottle();
            for (int i = 0; i < SoundThrottle.MaxPerSecond; i++) Assert.True(t.Allow("s" + i, 0.5f));
            Assert.False(t.Allow("extra", 0.6f));
            Assert.True(t.Allow("extra", 0.6f, stop: true));
            Assert.True(t.Allow("extra", 1.6f));
        }

        [Fact]
        public void Empty_name_is_refused()
        {
            Assert.False(new SoundThrottle().Allow("", 0f));
        }

        [Fact]
        public void PlayerSound_round_trips_and_caps_name()
        {
            var (t, m) = MessageCodec.Decode(MessageCodec.Encode(MessageType.PlayerSound,
                new PlayerSound { Name = new string('a', 100), Volume = 0.75f, Stop = true }));
            Assert.Equal(MessageType.PlayerSound, t);
            var s = Assert.IsType<PlayerSound>(m);
            Assert.Equal(PlayerSound.MaxNameLength, s.Name.Length);
            Assert.Equal(0.75f, s.Volume);
            Assert.True(s.Stop);
        }
    }
}
