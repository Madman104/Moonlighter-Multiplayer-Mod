namespace Coop.Core.Sync
{
    /// <summary>
    /// Where a player's Will is, for deciding whether to draw the other player.
    /// Town = 1; a dungeon floor = 100 + culture * 10 + floor (both players share the floor
    /// layout, so world positions match); anything else (menu, loading) = None.
    /// </summary>
    public static class LocationId
    {
        public const int None = -1;
        public const int Town = 1;

        public static int From(bool hasWill, bool townLoaded, bool dungeonLoaded, int culture, int floor)
        {
            if (!hasWill) return None;
            if (dungeonLoaded)
                return culture >= 0 && floor >= 0 ? 100 + culture * 10 + floor : None;
            return townLoaded ? Town : None;
        }
    }
}
