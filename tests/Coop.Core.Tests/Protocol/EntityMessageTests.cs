using Coop.Core.Protocol;
using Coop.Core.Sync;
using Xunit;

namespace Coop.Core.Tests.Protocol
{
    public class EntityMessageTests
    {
        [Fact]
        public void EntityStates_round_trip()
        {
            var msg = new EntityStates
            {
                Group = EntityStates.ShopVisitors,
                Extra = 1,
                Key = -123456,
                Entities = new[]
                {
                    new EntityState
                    {
                        Id = 7, X = 1.5f, Y = -2f,
                        Layers = new[] { new SpriteLayer { Slot = 2, SpriteId = 40001, Visible = true, FlipX = true, SortingOrder = -3, LocalX = 0.5f, LocalY = 4f } },
                    },
                    new EntityState { Id = 9, X = 3f, Y = 4f, Layers = new SpriteLayer[0] },
                },
            };
            var (t, m) = MessageCodec.Decode(MessageCodec.Encode(MessageType.EntityStates, msg));
            Assert.Equal(MessageType.EntityStates, t);
            var e = Assert.IsType<EntityStates>(m);
            Assert.Equal(EntityStates.ShopVisitors, e.Group);
            Assert.Equal(1, e.Extra);
            Assert.Equal(-123456, e.Key);
            Assert.Equal(2, e.Entities.Length);
            Assert.Equal(7, e.Entities[0].Id);
            Assert.Equal(-2f, e.Entities[0].Y);
            var l = e.Entities[0].Layers[0];
            Assert.Equal(40001, l.SpriteId);
            Assert.True(l.FlipX);
            Assert.Equal(-3, l.SortingOrder);
            Assert.Empty(e.Entities[1].Layers);
        }

        [Fact]
        public void EnemyOwners_round_trip()
        {
            var (t, m) = MessageCodec.Decode(MessageCodec.Encode(MessageType.EnemyOwners,
                new EnemyOwners { Key = 42, GuestOwned = new ushort[] { 1, 5, 300 } }));
            Assert.Equal(MessageType.EnemyOwners, t);
            var o = Assert.IsType<EnemyOwners>(m);
            Assert.Equal(42, o.Key);
            Assert.Equal(new ushort[] { 1, 5, 300 }, o.GuestOwned);
        }

        [Fact]
        public void CashSell_has_no_payload()
        {
            var (t, m) = MessageCodec.Decode(MessageCodec.Encode(MessageType.CashSell, null));
            Assert.Equal(MessageType.CashSell, t);
            Assert.Null(m);
        }

        [Fact]
        public void Allocator_can_start_at_an_offset()
        {
            var a = new SpriteIdAllocator(40000);
            Assert.Equal(40000, a.GetOrAdd("x", out bool isNew));
            Assert.True(isNew);
            Assert.Equal(40001, a.GetOrAdd("y", out _));
            a.Reset();
            Assert.Equal(40000, a.GetOrAdd("z", out _));
        }
    }
}
