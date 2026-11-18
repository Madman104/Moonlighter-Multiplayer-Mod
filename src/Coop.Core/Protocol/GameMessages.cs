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
        /// <summary>The host's calendar day count (MoonlighterCalendar.TotalDays).</summary>
        public int TotalDays;
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

namespace Coop.Core.Protocol
{
    /// <summary>
    /// Gzipped UTF-8 text (game JSON). Used for the host's whole save slot (WorldSlot) and the
    /// shop's showcases and chests (ShopState).
    /// </summary>
    public class PackedText
    {
        public byte[] Data = new byte[0];

        public static byte[] Pack(string text)
        {
            var raw = System.Text.Encoding.UTF8.GetBytes(text ?? "");
            using (var ms = new System.IO.MemoryStream())
            {
                using (var gz = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionLevel.Optimal, true))
                    gz.Write(raw, 0, raw.Length);
                return ms.ToArray();
            }
        }

        public static string Unpack(byte[] data)
        {
            using (var ms = new System.IO.MemoryStream(data ?? new byte[0]))
            using (var gz = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionMode.Decompress))
            using (var rd = new System.IO.StreamReader(gz, System.Text.Encoding.UTF8))
                return rd.ReadToEnd();
        }
    }

    /// <summary>Host → guest: the host's save slot, so the guest plays in the host's world.</summary>
    public sealed class WorldSlot : PackedText { }

    /// <summary>Either way: the shop's showcases (with prices) and chests.</summary>
    public sealed class ShopState : PackedText { }
}

namespace Coop.Core.Protocol
{
    /// <summary>One drawn-only copy of something the other game simulates (e.g. a shop customer).</summary>
    public sealed class EntityState
    {
        public ushort Id;
        public float X;
        public float Y;
        public SpriteLayer[] Layers = new SpriteLayer[0];
    }

    /// <summary>Host → guest, ~10 times a second: everything in one group, so missing ids are gone.</summary>
    public sealed class EntityStates
    {
        public const byte ShopVisitors = 1;
        public const byte Enemies = 2;
        public const byte Citizens = 3;

        public byte Group;
        /// <summary>Group-specific key. Enemies: EnemyOwnership.RoomKey of the room.</summary>
        public int Key;
        /// <summary>Group-specific flag. Shop: 1 = a customer is waiting at the cash register.</summary>
        public byte Extra;
        public EntityState[] Entities = new EntityState[0];
    }
}

namespace Coop.Core.Protocol
{
    /// <summary>Host → guest: in this room, which enemies the guest's game runs (all others: host's).</summary>
    public sealed class EnemyOwners
    {
        public int Key;
        public ushort[] GuestOwned = new ushort[0];
    }
}

namespace Coop.Core.Protocol
{
    /// <summary>Each player's display name (Steam name), sent once after connecting.</summary>
    public sealed class PlayerName
    {
        public const int MaxLength = 24;
        public string Name = "";

        /// <summary>Trimmed, one line, at most MaxLength characters; "Player" if empty.</summary>
        public static string Clean(string raw)
        {
            var s = (raw ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (s.Length == 0) return "Player";
            return s.Length > MaxLength ? s.Substring(0, MaxLength) : s;
        }
    }
}
