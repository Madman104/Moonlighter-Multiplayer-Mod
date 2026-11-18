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
        /// <summary>Host's max health per id (difficulty/NG+ can differ per player); -1 = none.</summary>
        public float[] Max;
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
        public const byte Projectiles = 4;

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
    /// <summary>Host → guests: in this room, which player's game runs which enemy (unlisted: host's).</summary>
    public sealed class EnemyOwners
    {
        public int Key;
        public ushort[] Ids = new ushort[0];
        public byte[] Owners = new byte[0];
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

namespace Coop.Core.Protocol
{
    /// <summary>Host → guest: a message another player sent, forwarded with its sender's number.</summary>
    public sealed class Relay
    {
        public byte From;
        public byte[] Inner = new byte[0];
    }
}

namespace Coop.Core.Protocol
{
    /// <summary>The sender's Will played (or stopped) a sound effect: footstep, swing, hurt...</summary>
    public sealed class PlayerSound
    {
        public const int MaxNameLength = 64;
        public string Name = "";
        public float Volume = 1f;
        public bool Stop;
    }
}

namespace Coop.Core.Protocol
{
    /// <summary>
    /// An item landed on a dungeon floor in the sender's game (enemy loot, a broken pot...).
    /// Everyone else puts the same item there. Id = (creator's player number &lt;&lt; 24) | counter.
    /// </summary>
    public sealed class FloorItemSpawn
    {
        public int Id;
        public int RoomKey;
        public float X;
        public float Y;
        /// <summary>The game's ItemSaveInfo as JSON.</summary>
        public string Json = "";
    }

    /// <summary>A shared floor item was picked up: Remaining 0 = gone, otherwise what's left of the stack.</summary>
    public sealed class FloorItemTaken
    {
        public int Id;
        public int Remaining;
    }
}

namespace Coop.Core.Protocol
{
    /// <summary>A player opened (or closed) a dungeon chest; others can't open it meanwhile.</summary>
    public sealed class ChestUse
    {
        public int RoomKey;
        public byte Index; // in room.roomChests (spawn order)
        public bool Open;
    }

    /// <summary>A dungeon chest's whole content, as a JSON list of the game's ItemSaveInfo.</summary>
    public sealed class ChestContents
    {
        public int RoomKey;
        public byte Index;
        public string Json = "";
    }

    /// <summary>A pot/crate/skeleton broke; X, Y are its position relative to the room.</summary>
    public sealed class BreakableBroken
    {
        public int RoomKey;
        public float X;
        public float Y;
    }
}

namespace Coop.Core.Protocol
{
    /// <summary>
    /// State of a room object, found by its position relative to the room.
    /// Shrine: Value 1 = used (doors opened). Pool: Value = fraction of healing left (0..1).
    /// </summary>
    public sealed class RoomObject
    {
        public const byte Shrine = 1;
        public const byte Pool = 2;

        public int RoomKey;
        public byte Kind;
        public float X;
        public float Y;
        public float Value;
    }
}

namespace Coop.Core.Protocol
{
    /// <summary>
    /// An enemy run by the sender's game hit another player's figure there. Sent to everyone;
    /// only the Target player takes the damage.
    /// </summary>
    public sealed class PartnerHit
    {
        public byte Target;
        public float Damage;
        public string Source = "";
    }

    /// <summary>An enemy summoned another (AI spawn) in the sender's game; others spawn the same one.</summary>
    public sealed class SummonSpawned
    {
        public int RoomKey;
        public byte Id;
        public string Name = "";
        public int Tile;
        public float X;
        public float Y;
    }

    /// <summary>A summoned enemy vanished without dying (timed out, merged...): others remove theirs.</summary>
    public sealed class SummonGone
    {
        public int RoomKey;
        public byte Id;
    }
}

namespace Coop.Core.Protocol
{
    /// <summary>Either way: the town's upgrades and the banker, as JSON (last change wins).</summary>
    public sealed class TownState : PackedText { }
}
