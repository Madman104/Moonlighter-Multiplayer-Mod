namespace Coop.Core.Dungeon
{
    public static class EnemyHealthRules
    {
        /// <summary>User decision: enemies have double health in co-op.</summary>
        public const float CoopMaxHealthFactor = 2f;

        /// <summary>Correct the client's enemy health only when it is clearly off.</summary>
        public static bool NeedsCorrection(float local, float host) => System.Math.Abs(local - host) > 0.5f;
    }

    /// <summary>Deterministic per-room seed, so both games make the same random spawn choices.</summary>
    public static class RoomSeed
    {
        public static int For(int culture, int floor, int x, int y, bool secret)
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + culture;
                h = h * 31 + floor;
                h = h * 31 + x;
                h = h * 31 + y;
                h = h * 31 + (secret ? 1 : 0);
                return h;
            }
        }
    }
}
