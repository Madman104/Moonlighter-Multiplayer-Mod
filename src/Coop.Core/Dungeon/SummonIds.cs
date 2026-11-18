namespace Coop.Core.Dungeon
{
    /// <summary>
    /// Enemy ids (one byte, per room): room spawns count from 0, summons use 100-199 in a block
    /// of 25 per player (so two games summoning at once never clash), mimics 200+.
    /// </summary>
    public static class SummonIds
    {
        public const int Base = 100;
        public const int PerPlayer = 25;

        public static int For(int player, int n) => Base + (player & 3) * PerPlayer + (n % PerPlayer);
    }
}
