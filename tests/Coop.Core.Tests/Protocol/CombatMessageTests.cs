using Coop.Core.Dungeon;
using Coop.Core.Protocol;
using Xunit;

namespace Coop.Core.Tests.Protocol
{
    public class CombatMessageTests
    {
        private static T RoundTrip<T>(MessageType type, T msg)
        {
            var (t, m) = MessageCodec.Decode(MessageCodec.Encode(type, msg));
            Assert.Equal(type, t);
            return Assert.IsType<T>(m);
        }

        [Fact]
        public void PartnerHit_round_trips()
        {
            var h = RoundTrip(MessageType.PartnerHit, new PartnerHit { Target = 2, Damage = 12.5f, Source = "Slime" });
            Assert.Equal(2, h.Target);
            Assert.Equal(12.5f, h.Damage);
            Assert.Equal("Slime", h.Source);
        }

        [Fact]
        public void SummonSpawned_round_trips()
        {
            var s = RoundTrip(MessageType.SummonSpawned, new SummonSpawned { RoomKey = 4, Id = 130, Name = "BabySlime", Tile = 77, X = 3f, Y = -4f });
            Assert.Equal(4, s.RoomKey);
            Assert.Equal(130, s.Id);
            Assert.Equal("BabySlime", s.Name);
            Assert.Equal(77, s.Tile);
            Assert.Equal(3f, s.X);
            Assert.Equal(-4f, s.Y);
        }

        [Fact]
        public void TownState_round_trips()
        {
            var t = RoundTrip(MessageType.TownState, new TownState { Data = PackedText.Pack("{\"shop\":3}") });
            Assert.Equal("{\"shop\":3}", PackedText.Unpack(t.Data));
        }

        [Fact]
        public void SpriteRequest_and_data_round_trip()
        {
            var r = RoundTrip(MessageType.SpriteRequest, new SpriteRequest { Key = "Atlas/BroomSpear_1" });
            Assert.Equal("Atlas/BroomSpear_1", r.Key);
            var d = RoundTrip(MessageType.SpriteData, new SpriteData { Key = "k", PivotX = 0.5f, PivotY = 0.25f, PixelsPerUnit = 1f, Png = new byte[] { 1, 2, 3 } });
            Assert.Equal("k", d.Key);
            Assert.Equal(0.25f, d.PivotY);
            Assert.Equal(new byte[] { 1, 2, 3 }, d.Png);
        }

        [Fact]
        public void ShopBusy_round_trips()
        {
            var b = RoundTrip(MessageType.ShopBusy, new ShopBusy { Holder = 2, Open = true });
            Assert.Equal(2, b.Holder);
            Assert.True(b.Open);
        }

        [Fact]
        public void SummonGone_round_trips()
        {
            var g = RoundTrip(MessageType.SummonGone, new SummonGone { RoomKey = 4, Id = 101 });
            Assert.Equal(4, g.RoomKey);
            Assert.Equal(101, g.Id);
        }

        [Theory]
        [InlineData(0, 0, 100)]
        [InlineData(1, 0, 125)]
        [InlineData(3, 24, 199)]
        [InlineData(1, 25, 125)]
        public void Summon_ids_stay_in_each_players_block(int player, int n, int expected)
        {
            Assert.Equal(expected, SummonIds.For(player, n));
        }
    }
}
