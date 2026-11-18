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
    }
}
