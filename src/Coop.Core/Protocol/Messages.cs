namespace Coop.Core.Protocol
{
    /// <summary>First message a client sends after connecting.</summary>
    public sealed class Hello
    {
        public string ModVersion;
        public string GameVersion;
        public string[] OwnedDlc;
    }

    /// <summary>Host accepts the client. Host is player 0, client is player 1.</summary>
    public sealed class Welcome
    {
        public byte PlayerIndex;
    }

    public enum RejectReason : byte
    {
        ModVersion = 1,
        GameVersion = 2,
        Dlc = 3,
        Full = 4,
    }

    /// <summary>Host refuses the client; the connection closes after this.</summary>
    public sealed class Reject
    {
        public RejectReason Reason;
        public string Detail;
    }

    /// <summary>One SpriteRenderer of a player's Will, mirrored by the remote puppet.</summary>
    public sealed class SpriteLayer
    {
        public byte Slot;
        public ushort SpriteId;
        public bool Visible;
        public bool FlipX;
        public short SortingOrder;
        public float LocalX;
        public float LocalY;
    }

    /// <summary>Snapshot of one player's Will, sent ~20 times per second.</summary>
    public sealed class PlayerState
    {
        public uint Tick;
        public int SceneId;
        public float X;
        public float Y;
        public float Z;
        public SpriteLayer[] Layers;
        public float Hp;
        public float MaxHp;
        public bool Ghost;
    }

    /// <summary>Keep-alive. Also used as the body of Pong.</summary>
    public sealed class Ping
    {
        public uint Nonce;
    }
}

namespace Coop.Core.Protocol
{
    /// <summary>
    /// Tells the peer which sprite a sender-assigned id stands for. Sent reliably the first
    /// time an id is used. Key = "textureName/spriteName".
    /// </summary>
    public sealed class SpriteDef
    {
        public ushort Id;
        public string Key;
    }
}
