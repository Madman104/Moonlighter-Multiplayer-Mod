using Coop.Core.Dungeon;
using Coop.Core.Protocol;
using Xunit;

namespace Coop.Core.Tests.Dungeon
{
    public class GhostTests
    {
        [Fact]
        public void Dying_while_partner_alive_in_shared_dungeon_makes_a_ghost()
        {
            Assert.True(GhostRules.ShouldBecomeGhost(connected: true, inSharedDungeon: true, partnerHp: 40f, partnerGhost: false));
        }

        [Fact]
        public void Dying_when_partner_is_ghost_or_down_is_a_real_death()
        {
            Assert.False(GhostRules.ShouldBecomeGhost(true, true, partnerHp: 40f, partnerGhost: true));
            Assert.False(GhostRules.ShouldBecomeGhost(true, true, partnerHp: 0f, partnerGhost: false));
        }

        [Fact]
        public void Outside_shared_dungeon_or_offline_death_is_normal()
        {
            Assert.False(GhostRules.ShouldBecomeGhost(false, true, 40f, false));
            Assert.False(GhostRules.ShouldBecomeGhost(true, false, 40f, false));
        }

        [Fact]
        public void Living_player_in_full_pool_revives_ghost_partner()
        {
            Assert.True(GhostRules.CanRevive(localGhost: false, partnerGhost: true, poolHealth: 10f));
            Assert.False(GhostRules.CanRevive(localGhost: true, partnerGhost: true, poolHealth: 10f));
            Assert.False(GhostRules.CanRevive(false, false, 10f));
            Assert.False(GhostRules.CanRevive(false, true, 0f));
        }

        [Fact]
        public void Revive_restores_half_health()
        {
            Assert.Equal(50f, GhostRules.ReviveHealth(100f));
        }

        [Fact]
        public void PlayerState_carries_health_and_ghost_flag()
        {
            var s = new PlayerState { Tick = 1, SceneId = 1, Hp = 37.5f, MaxHp = 120f, Ghost = true, Layers = new SpriteLayer[0] };
            var (_, m) = MessageCodec.Decode(MessageCodec.Encode(MessageType.PlayerState, s));
            var d = (PlayerState)m;
            Assert.Equal(37.5f, d.Hp);
            Assert.Equal(120f, d.MaxHp);
            Assert.True(d.Ghost);
        }

        [Fact]
        public void Revive_message_round_trips()
        {
            var (t, m) = MessageCodec.Decode(MessageCodec.Encode(MessageType.Revive, null));
            Assert.Equal(MessageType.Revive, t);
            Assert.Null(m);
        }
    }
}
