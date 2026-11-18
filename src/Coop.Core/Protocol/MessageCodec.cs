using System;
using System.IO;
using System.Text;

namespace Coop.Core.Protocol
{
    /// <summary>
    /// Encodes messages as: 1 type byte + body. Body uses BinaryWriter (little-endian,
    /// strings length-prefixed UTF-8).
    /// </summary>
    public static class MessageCodec
    {
        public static byte[] Encode(MessageType type, object msg)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms, Encoding.UTF8))
            {
                w.Write((byte)type);
                switch (type)
                {
                    case MessageType.Hello:
                        var h = (Hello)msg;
                        w.Write(h.ModVersion ?? "");
                        w.Write(h.GameVersion ?? "");
                        var dlc = h.OwnedDlc ?? new string[0];
                        w.Write((ushort)dlc.Length);
                        foreach (var d in dlc) w.Write(d ?? "");
                        break;
                    case MessageType.Welcome:
                        w.Write(((Welcome)msg).PlayerIndex);
                        break;
                    case MessageType.Reject:
                        var r = (Reject)msg;
                        w.Write((byte)r.Reason);
                        w.Write(r.Detail ?? "");
                        break;
                    case MessageType.PlayerState:
                        WritePlayerState(w, (PlayerState)msg);
                        break;
                    case MessageType.Ping:
                    case MessageType.Pong:
                        w.Write(((Ping)msg).Nonce);
                        break;
                    case MessageType.Bye:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown message type");
                }
                w.Flush();
                return ms.ToArray();
            }
        }

        public static (MessageType type, object msg) Decode(byte[] payload)
        {
            if (payload == null || payload.Length == 0)
                throw new InvalidDataException("Empty message");
            var type = (MessageType)payload[0];
            try
            {
                using (var ms = new MemoryStream(payload, 1, payload.Length - 1))
                using (var rd = new BinaryReader(ms, Encoding.UTF8))
                {
                    switch (type)
                    {
                        case MessageType.Hello:
                            var h = new Hello { ModVersion = rd.ReadString(), GameVersion = rd.ReadString() };
                            int n = rd.ReadUInt16();
                            h.OwnedDlc = new string[n];
                            for (int i = 0; i < n; i++) h.OwnedDlc[i] = rd.ReadString();
                            return (type, h);
                        case MessageType.Welcome:
                            return (type, new Welcome { PlayerIndex = rd.ReadByte() });
                        case MessageType.Reject:
                            return (type, new Reject { Reason = (RejectReason)rd.ReadByte(), Detail = rd.ReadString() });
                        case MessageType.PlayerState:
                            return (type, ReadPlayerState(rd));
                        case MessageType.Ping:
                        case MessageType.Pong:
                            return (type, new Ping { Nonce = rd.ReadUInt32() });
                        case MessageType.Bye:
                            return (type, null);
                        default:
                            throw new InvalidDataException("Unknown message type " + payload[0]);
                    }
                }
            }
            catch (EndOfStreamException e)
            {
                throw new InvalidDataException("Truncated " + type + " message", e);
            }
        }

        private static void WritePlayerState(BinaryWriter w, PlayerState s)
        {
            w.Write(s.Tick);
            w.Write(s.SceneId);
            w.Write(s.X);
            w.Write(s.Y);
            w.Write(s.Z);
            var layers = s.Layers ?? new SpriteLayer[0];
            w.Write((byte)layers.Length);
            foreach (var l in layers)
            {
                w.Write(l.Slot);
                w.Write(l.SpriteId);
                byte flags = (byte)((l.Visible ? 1 : 0) | (l.FlipX ? 2 : 0));
                w.Write(flags);
                w.Write(l.SortingOrder);
                w.Write(l.LocalX);
                w.Write(l.LocalY);
            }
        }

        private static PlayerState ReadPlayerState(BinaryReader rd)
        {
            var s = new PlayerState
            {
                Tick = rd.ReadUInt32(),
                SceneId = rd.ReadInt32(),
                X = rd.ReadSingle(),
                Y = rd.ReadSingle(),
                Z = rd.ReadSingle(),
            };
            int n = rd.ReadByte();
            s.Layers = new SpriteLayer[n];
            for (int i = 0; i < n; i++)
            {
                var l = new SpriteLayer { Slot = rd.ReadByte(), SpriteId = rd.ReadUInt16() };
                byte flags = rd.ReadByte();
                l.Visible = (flags & 1) != 0;
                l.FlipX = (flags & 2) != 0;
                l.SortingOrder = rd.ReadInt16();
                l.LocalX = rd.ReadSingle();
                l.LocalY = rd.ReadSingle();
                s.Layers[i] = l;
            }
            return s;
        }
    }
}
