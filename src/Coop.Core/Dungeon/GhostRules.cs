namespace Coop.Core.Dungeon
{
    /// <summary>
    /// Design: a player who dies in a dungeon while the partner is alive becomes a ghost;
    /// the living partner revives them at a healing pool, which uses the pool up.
    /// </summary>
    public static class GhostRules
    {
        public static bool ShouldBecomeGhost(bool connected, bool inSharedDungeon, float partnerHp, bool partnerGhost)
            => connected && inSharedDungeon && partnerHp > 0f && !partnerGhost;

        public static bool CanRevive(bool localGhost, bool partnerGhost, float poolHealth)
            => !localGhost && partnerGhost && poolHealth > 0f;

        public static float ReviveHealth(float maxHp) => maxHp * 0.5f;
    }
}
