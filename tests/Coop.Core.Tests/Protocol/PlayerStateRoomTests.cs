using Coop.Core.Protocol;
using Xunit;

namespace Coop.Core.Tests.Protocol
{
    public class PlayerStateRoomTests
    {
        [Fact]
        public void Room_and_sorting_layer_round_trip()
        {
            var sent = new PlayerState
            {
                Tick = 3, SceneId = 12, X = 1f, Y = 2f, Z = 0f, Hp = 50f, MaxHp = 100f, RoomKey = 123456,
                Layers = new[] { new SpriteLayer { Slot = 4, SpriteId = 9, Visible = true, SortingOrder = -2, SortingLayerId = 777, LocalX = 1f, LocalY = 3f } },
            };
            var (t, m) = MessageCodec.Decode(MessageCodec.Encode(MessageType.PlayerState, sent));
            Assert.Equal(MessageType.PlayerState, t);
            var got = Assert.IsType<PlayerState>(m);
            Assert.Equal(123456, got.RoomKey);
            Assert.Equal(777, got.Layers[0].SortingLayerId);
            Assert.Equal(-2, got.Layers[0].SortingOrder);
        }

        [Fact]
        public void No_room_by_default()
        {
            Assert.Equal(PlayerState.NoRoom, new PlayerState().RoomKey);
        }
    }
}
