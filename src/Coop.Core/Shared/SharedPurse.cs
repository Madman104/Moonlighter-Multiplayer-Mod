using System;

namespace Coop.Core.Shared
{
    /// <summary>
    /// Host-side bookkeeping for the shared purse. The game clamps gold at 0, so when both
    /// players spend at the same moment with too little gold, the part that couldn't be paid is
    /// kept as debt and taken from the next income instead of being lost (a free purchase).
    /// </summary>
    public sealed class SharedPurse
    {
        public int Debt { get; private set; }

        /// <summary>Gold after applying the partner's change; any shortfall becomes debt.</summary>
        public int ApplyRemoteDelta(int current, int delta)
        {
            if (delta > 0) delta -= SettleIncome(delta);
            int next = current + delta;
            if (next >= 0) return next;
            Debt += -next;
            return 0;
        }

        /// <summary>How much of an income should go to paying the debt (taken back right away).</summary>
        public int SettleIncome(int income)
        {
            if (income <= 0 || Debt <= 0) return 0;
            int pay = Math.Min(Debt, income);
            Debt -= pay;
            return pay;
        }

        public void Reset() => Debt = 0;
    }
}
