using Coop.Core.Shared;
using Xunit;

namespace Coop.Core.Tests.Shared
{
    public class SharedPurseTests
    {
        [Fact]
        public void Remote_spend_within_gold_leaves_no_debt()
        {
            var p = new SharedPurse();
            Assert.Equal(40, p.ApplyRemoteDelta(100, -60));
            Assert.Equal(0, p.Debt);
        }

        [Fact]
        public void Remote_spend_beyond_gold_becomes_debt()
        {
            var p = new SharedPurse();
            Assert.Equal(0, p.ApplyRemoteDelta(50, -80));
            Assert.Equal(30, p.Debt);
        }

        [Fact]
        public void Income_pays_debt_first()
        {
            var p = new SharedPurse();
            p.ApplyRemoteDelta(0, -30);
            Assert.Equal(20, p.SettleIncome(20));
            Assert.Equal(10, p.Debt);
            Assert.Equal(10, p.SettleIncome(500));
            Assert.Equal(0, p.Debt);
            Assert.Equal(0, p.SettleIncome(5));
        }

        [Fact]
        public void Remote_income_also_pays_debt()
        {
            var p = new SharedPurse();
            p.ApplyRemoteDelta(0, -30);
            Assert.Equal(15, p.ApplyRemoteDelta(0, 45));
            Assert.Equal(0, p.Debt);
        }

        [Fact]
        public void Reset_clears_debt()
        {
            var p = new SharedPurse();
            p.ApplyRemoteDelta(0, -30);
            p.Reset();
            Assert.Equal(0, p.Debt);
        }
    }
}
