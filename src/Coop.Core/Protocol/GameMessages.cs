namespace Coop.Core.Protocol
{
    /// <summary>Host's generated dungeon floor, as the game's own DungeonSaveInfo JSON.</summary>
    public sealed class FloorLayout
    {
        public byte Culture;
        public byte Floor;
        public string Json;
    }

    /// <summary>A player entered a dungeon or went to another floor.</summary>
    public sealed class DungeonTravel
    {
        public byte Culture;
        public byte Level;
        public bool Replay;
    }

    /// <summary>A player went back to town from a dungeon.</summary>
    public sealed class TownTravel
    {
        public bool FromEmblem;
    }

    /// <summary>
    /// A player changed dungeon room. Rooms are grid coordinates; the door is an index into
    /// roomDoors of the room at (FromX, FromY). Special = SpecialChangeRoomTo (holes, secrets).
    /// </summary>
    public sealed class RoomChange
    {
        public short FromX;
        public short FromY;
        public short ToX;
        public short ToY;
        public sbyte DoorIndex;
        public bool Special;
        public bool TeleportToLastDoor;
        public bool FromSecret;
        public bool ToSecret;
    }
}

namespace Coop.Core.Protocol
{
    /// <summary>An enemy lost health on the sender's game. Enemy = index in room.roomEnemies.</summary>
    public sealed class EnemyDamage
    {
        public short X;
        public short Y;
        public bool Secret;
        public byte Index;
        public float Delta;
    }

    /// <summary>Host's health for every enemy of one room (drift correction).</summary>
    public sealed class EnemyHealth
    {
        public short X;
        public short Y;
        public bool Secret;
        public float[] Hp;
    }
}

namespace Coop.Core.Protocol
{
    /// <summary>The sender's game gained or spent gold (shared purse).</summary>
    public sealed class GoldDelta
    {
        public int Delta;
    }

    /// <summary>Host's gold total (sent on connect and periodically for correction).</summary>
    public sealed class GoldTotal
    {
        public int Value;
    }
}

namespace Coop.Core.Protocol
{
    /// <summary>Host's time of day (the game's DayTime enum value). FromBed = the host just slept.</summary>
    public sealed class DayTimeSync
    {
        public byte Value;
        public bool FromBed;
    }
}

namespace Coop.Core.Protocol
{
    /// <summary>The healing pool in this room was used up (to revive a ghost).</summary>
    public sealed class PoolSpent
    {
        public short X;
        public short Y;
        public bool Secret;
    }
}

namespace Coop.Core.Protocol
{
    /// <summary>The sender's whole bag (shared inventory), as the game's ItemSaveInfo list JSON.</summary>
    public sealed class BagSnapshot
    {
        public string Json;
    }
}
