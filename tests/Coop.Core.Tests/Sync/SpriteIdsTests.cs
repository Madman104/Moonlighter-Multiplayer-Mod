using Coop.Core.Sync;
using Xunit;

namespace Coop.Core.Tests.Sync
{
    public class SpriteIdsTests
    {
        [Fact]
        public void First_use_assigns_new_id_starting_at_one()
        {
            var a = new SpriteIdAllocator();
            Assert.Equal(1, a.GetOrAdd("sheet/walk_0", out var isNew));
            Assert.True(isNew);
            Assert.Equal(2, a.GetOrAdd("sheet/walk_1", out isNew));
            Assert.True(isNew);
        }

        [Fact]
        public void Repeat_use_returns_same_id_and_is_not_new()
        {
            var a = new SpriteIdAllocator();
            a.GetOrAdd("x", out _);
            Assert.Equal(1, a.GetOrAdd("x", out var isNew));
            Assert.False(isNew);
        }

        [Fact]
        public void Null_or_empty_key_is_id_zero()
        {
            var a = new SpriteIdAllocator();
            Assert.Equal(0, a.GetOrAdd(null, out var isNew));
            Assert.False(isNew);
            Assert.Equal(0, a.GetOrAdd("", out _));
        }

        [Fact]
        public void Reset_starts_numbering_again()
        {
            var a = new SpriteIdAllocator();
            a.GetOrAdd("x", out _);
            a.GetOrAdd("y", out _);
            a.Reset();
            Assert.Equal(1, a.GetOrAdd("y", out var isNew));
            Assert.True(isNew);
        }

        [Fact]
        public void Name_map_resolves_defined_ids_only()
        {
            var m = new SpriteNameMap();
            m.Define(3, "sheet/a");
            Assert.Equal("sheet/a", m.GetKey(3));
            Assert.Null(m.GetKey(0));
            Assert.Null(m.GetKey(4));
        }

        [Fact]
        public void Name_map_redefinition_overwrites()
        {
            var m = new SpriteNameMap();
            m.Define(1, "a");
            m.Define(1, "b");
            Assert.Equal("b", m.GetKey(1));
        }
    }
}
