using System;

namespace Coop.Core.Dungeon
{
    /// <summary>
    /// Stage 3b: each enemy is run (AI, movement, attacks) by the game of the player closer to
    /// it; the other game shows it where the runner says. The host decides, with a margin so
    /// enemies don't flip back and forth between two players standing at similar distances.
    /// </summary>
    public static class EnemyOwnership
    {
        public const float DefaultMargin = 24f;

        /// <summary>Distances are squared; float.PositiveInfinity = that player isn't in the room.</summary>
        public static bool GuestShouldOwn(bool guestOwnsNow, float hostDistSq, float guestDistSq, float margin)
        {
            if (float.IsInfinity(guestDistSq) || float.IsNaN(guestDistSq)) return false;
            if (float.IsInfinity(hostDistSq)) return true;
            float host = (float)Math.Sqrt(hostDistSq), guest = (float)Math.Sqrt(guestDistSq);
            return guestOwnsNow ? guest - host <= margin : host - guest > margin;
        }

        /// <summary>One number per room (x, y, secret), for tagging per-room messages.</summary>
        public static int RoomKey(int x, int y, bool secret)
            => ((x & 0x7FFF) << 16) | ((y & 0x7FFF) << 1) | (secret ? 1 : 0);
    }
}
