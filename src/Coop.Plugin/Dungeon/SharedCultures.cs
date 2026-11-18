namespace Coop.Plugin.Dungeon
{
    /// <summary>
    /// Dungeons built from rooms by DungeonGenerator: their floors, enemies and ghosts are
    /// shared. The four main dungeons and the Unknown (fifth) dungeon. The Wanderer DLC
    /// dungeons use their own systems: travel is mirrored there, enemies are not.
    /// </summary>
    internal static class SharedCultures
    {
        public static bool Contains(ItemMaster.Culture c)
            => c >= ItemMaster.Culture.Golem && c <= ItemMaster.Culture.Unknown;

        public static bool InSharedDungeon()
        {
            var gm = GameManager.Instance;
            var dm = DungeonManager.Instance;
            return gm != null && dm != null && gm.IsDungeonSceneLoaded() && Contains(dm.culture);
        }
    }
}
