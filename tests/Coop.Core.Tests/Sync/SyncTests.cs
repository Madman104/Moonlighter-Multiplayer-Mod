using Coop.Core.Session;
using Coop.Core.Sync;
using Xunit;

namespace Coop.Core.Tests.Sync
{
    public class SnapshotInterpolatorTests
    {
        [Fact]
        public void Empty_buffer_has_no_sample()
        {
            Assert.False(new SnapshotInterpolator(0.1).TrySample(1, out _, out _, out _));
        }

        [Fact]
        public void Samples_halfway_between_neighbours()
        {
            var i = new SnapshotInterpolator(0.1);
            i.Add(0.0, 0, 0, 0);
            i.Add(0.1, 10, 20, 2);
            Assert.True(i.TrySample(0.15, out var x, out var y, out var z));
            Assert.Equal(5f, x, 3);
            Assert.Equal(10f, y, 3);
            Assert.Equal(1f, z, 3);
        }

        [Fact]
        public void Starved_buffer_clamps_to_newest()
        {
            var i = new SnapshotInterpolator(0.1);
            i.Add(0.0, 0, 0, 0);
            i.Add(0.1, 10, 0, 0);
            Assert.True(i.TrySample(5.0, out var x, out _, out _));
            Assert.Equal(10f, x);
        }

        [Fact]
        public void Before_first_snapshot_clamps_to_oldest()
        {
            var i = new SnapshotInterpolator(0.1);
            i.Add(1.0, 3, 0, 0);
            i.Add(1.1, 9, 0, 0);
            Assert.True(i.TrySample(0.5, out var x, out _, out _));
            Assert.Equal(3f, x);
        }

        [Fact]
        public void Keeps_at_most_32_snapshots()
        {
            var i = new SnapshotInterpolator(0.0);
            for (int k = 0; k < 100; k++) i.Add(k, k, 0, 0);
            Assert.Equal(32, i.Count);
            Assert.True(i.TrySample(0, out var x, out _, out _));
            Assert.Equal(68f, x); // oldest kept
        }

        [Fact]
        public void Out_of_order_snapshot_is_dropped()
        {
            var i = new SnapshotInterpolator(0.0);
            i.Add(1.0, 1, 0, 0);
            i.Add(2.0, 2, 0, 0);
            i.Add(1.5, 99, 0, 0);
            Assert.True(i.TrySample(1.5, out var x, out _, out _));
            Assert.Equal(1.5f, x, 3);
        }

        [Fact]
        public void Clear_empties_buffer()
        {
            var i = new SnapshotInterpolator(0.1);
            i.Add(0, 1, 1, 1);
            i.Clear();
            Assert.False(i.TrySample(0, out _, out _, out _));
        }
    }

    public class PuppetVisibilityTests
    {
        [Fact]
        public void Shown_when_connected_in_same_scene()
        {
            Assert.True(PuppetVisibility.ShouldShow(5, 5, SessionState.Connected));
        }

        [Fact]
        public void Hidden_in_different_scenes()
        {
            Assert.False(PuppetVisibility.ShouldShow(5, 6, SessionState.Connected));
        }

        [Fact]
        public void Hidden_when_not_connected()
        {
            Assert.False(PuppetVisibility.ShouldShow(5, 5, SessionState.Closed));
            Assert.False(PuppetVisibility.ShouldShow(5, 5, SessionState.Handshaking));
        }

        [Fact]
        public void Hidden_when_either_side_has_no_will()
        {
            Assert.False(PuppetVisibility.ShouldShow(-1, -1, SessionState.Connected));
            Assert.False(PuppetVisibility.ShouldShow(5, -1, SessionState.Connected));
        }
    }
}
