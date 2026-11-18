using System;

namespace Coop.Core.Sync
{
    /// <summary>
    /// Colour of a player's name above their head: green at full health, yellow at half, red
    /// when low; grey for a ghost; white when health is unknown (e.g. in town before an update).
    /// </summary>
    public static class NameTint
    {
        public static (float r, float g, float b) For(float hp, float maxHp, bool ghost)
        {
            if (ghost) return (0.6f, 0.6f, 0.6f);
            if (hp < 0f || maxHp <= 0f) return (1f, 1f, 1f);
            float f = Math.Max(0f, Math.Min(1f, hp / maxHp));
            // red (low) -> yellow (half) -> green (full)
            return f >= 0.5f
                ? (1f - (f - 0.5f) * 2f * 0.7f, 0.95f, 0.25f)
                : (1f, 0.25f + f * 2f * 0.7f, 0.25f);
        }
    }
}
