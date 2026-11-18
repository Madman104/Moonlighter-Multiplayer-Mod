namespace Coop.Plugin.Dungeon
{
    /// <summary>
    /// Dungeons built from rooms by DungeonGenerator: their floors, enemies and ghosts are
    /// shared. The four main dungeons, the Unknown (fifth) dungeon and, unless turned off in
    /// the config ([Co-op] ShareWandererDungeons), the Wanderer DLC dungeon's regular floors.
    /// The DLC's in-between levels (lift, falling floor) are special scenes: travel is
    /// mirrored there, nothing else.
    /// </summary>
    internal static class SharedCultures
    {
        public static bool Contains(ItemMaster.Culture c)
        {
            if (c >= ItemMaster.Culture.Golem && c <= ItemMaster.Culture.Unknown) return true;
            if (c != ItemMaster.Culture.Wanderer || CoopConfig.ShareWandererDungeons == null || !CoopConfig.ShareWandererDungeons.Value) return false;
            var dm = DungeonManager.Instance;
            return dm == null || dm.intermediateInfo.dungeonType != DungeonManager.DungeonType.Intermediate;
        }

        public static bool InSharedDungeon()
        {
            if (Shared.WorldSync.InTutorial) return false;
            var gm = GameManager.Instance;
            var dm = DungeonManager.Instance;
            return gm != null && dm != null && gm.IsDungeonSceneLoaded() && Contains(dm.culture);
        }
    }
}
