using Coop.Core.Protocol;
using Coop.Core.Session;
using Xunit;

namespace Coop.Core.Tests.Session
{
    public class HandshakeRulesTests
    {
        private static Hello H(string mod = "0.1.0", string game = "1.15.8", params string[] dlc)
            => new Hello { ModVersion = mod, GameVersion = game, OwnedDlc = dlc };

        [Fact]
        public void Equal_hellos_are_compatible()
        {
            Assert.Null(HandshakeRules.Validate(H(dlc: "A"), H(dlc: "A"), ignoreDlc: false));
        }

        [Fact]
        public void Mod_version_mismatch_is_rejected()
        {
            var r = HandshakeRules.Validate(H("0.1.0"), H("0.1.1"), false);
            Assert.Equal(RejectReason.ModVersion, r.Reason);
            Assert.Contains("mod version", r.Detail);
            Assert.Contains("0.1.0", r.Detail);
            Assert.Contains("0.1.1", r.Detail);
        }

        [Fact]
        public void Game_version_mismatch_is_rejected()
        {
            var r = HandshakeRules.Validate(H(game: "1.15.8"), H(game: "1.15.7"), false);
            Assert.Equal(RejectReason.GameVersion, r.Reason);
        }

        [Fact]
        public void Mod_version_is_checked_before_game_version()
        {
            var r = HandshakeRules.Validate(H("0.1.0", "1"), H("0.2.0", "2"), false);
            Assert.Equal(RejectReason.ModVersion, r.Reason);
        }

        [Fact]
        public void Dlc_mismatch_is_rejected()
        {
            var r = HandshakeRules.Validate(H(dlc: "A"), H(), false);
            Assert.Equal(RejectReason.Dlc, r.Reason);
        }

        [Fact]
        public void Dlc_mismatch_is_allowed_when_ignored()
        {
            Assert.Null(HandshakeRules.Validate(H(dlc: "A"), H(), ignoreDlc: true));
        }

        [Fact]
        public void Dlc_order_does_not_matter()
        {
            Assert.Null(HandshakeRules.Validate(H(dlc: new[] { "A", "B" }), H(dlc: new[] { "B", "A" }), false));
        }

        [Fact]
        public void Null_dlc_list_counts_as_empty()
        {
            var a = new Hello { ModVersion = "x", GameVersion = "y", OwnedDlc = null };
            Assert.Null(HandshakeRules.Validate(a, H("x", "y"), false));
        }
    }
}
