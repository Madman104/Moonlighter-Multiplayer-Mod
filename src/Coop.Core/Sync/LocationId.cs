namespace Coop.Core.Sync
{
    /// <summary>
    /// Where a player's Will is, as far as showing the other player goes.
    /// Stage 1 shares only the town: dungeon layouts are still generated separately on each
    /// PC, so a Will in a dungeon is never shown to the other player yet.
    /// </summary>
    public static class LocationId
    {
        public const int None = -1;
        public const int Town = 1;

        public static int From(bool hasWill, bool townLoaded, bool dungeonLoaded)
        {
            if (!hasWill || dungeonLoaded || !townLoaded) return None;
            return Town;
        }
    }
}
