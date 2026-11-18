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
                    case MessageType.SpriteDef:
                        var sd = (SpriteDef)msg;
                        w.Write(sd.Id);
                        w.Write(sd.Key ?? "");
                        break;
                    case MessageType.Bye:
                        break;
                    case MessageType.Revive:
                    case MessageType.EmblemReturn:
                    case MessageType.CashSell:
                    case MessageType.ShopOpen:
                        break;
                    case MessageType.EntityStates:
                        var es = (EntityStates)msg;
                        var ents = es.Entities ?? new EntityState[0];
                        w.Write(es.Group);
                        w.Write(es.Extra);
                        w.Write(es.Key);
                        w.Write((ushort)System.Math.Min(ents.Length, ushort.MaxValue));
                        for (int i = 0; i < System.Math.Min(ents.Length, ushort.MaxValue); i++)
                        {
                            var en = ents[i];
                            w.Write(en.Id);
                            w.Write(en.X);
                            w.Write(en.Y);
                            var ls = en.Layers ?? new SpriteLayer[0];
                            w.Write((byte)System.Math.Min(ls.Length, 255));
                            for (int j = 0; j < System.Math.Min(ls.Length, 255); j++) WriteLayer(w, ls[j]);
                        }
                        break;
                    case MessageType.EnemyOwners:
                        var eo = (EnemyOwners)msg;
                        var owned = eo.GuestOwned ?? new ushort[0];
                        w.Write(eo.Key);
                        w.Write((ushort)Math.Min(owned.Length, ushort.MaxValue));
                        for (int i = 0; i < Math.Min(owned.Length, ushort.MaxValue); i++) w.Write(owned[i]);
                        break;
                    case MessageType.WorldSlot:
                    case MessageType.ShopState:
                        var pt = ((PackedText)msg).Data ?? new byte[0];
                        w.Write(pt.Length);
                        w.Write(pt);
                        break;
                    case MessageType.FloorLayout:
                        var fl = (FloorLayout)msg;
                        w.Write(fl.Culture);
                        w.Write(fl.Floor);
                        w.Write(fl.Json ?? "");
                        break;
                    case MessageType.DungeonTravel:
                        var dt = (DungeonTravel)msg;
                        w.Write(dt.Culture);
                        w.Write(dt.Level);
                        w.Write(dt.Replay);
                        break;
                    case MessageType.TownTravel:
                        w.Write(((TownTravel)msg).FromEmblem);
                        break;
                    case MessageType.RoomChange:
                        var rc = (RoomChange)msg;
                        w.Write(rc.FromX);
                        w.Write(rc.FromY);
                        w.Write(rc.ToX);
                        w.Write(rc.ToY);
                        w.Write(rc.DoorIndex);
                        w.Write((byte)((rc.Special ? 1 : 0) | (rc.TeleportToLastDoor ? 2 : 0) | (rc.FromSecret ? 4 : 0) | (rc.ToSecret ? 8 : 0)));
                        break;
                    case MessageType.EnemyDamage:
                        var ed = (EnemyDamage)msg;
                        w.Write(ed.X); w.Write(ed.Y); w.Write(ed.Secret); w.Write(ed.Index); w.Write(ed.Delta);
                        break;
                    case MessageType.EnemyHealth:
                        var eh = (EnemyHealth)msg;
                        w.Write(eh.X); w.Write(eh.Y); w.Write(eh.Secret);
                        var hp = eh.Hp ?? new float[0];
                        w.Write((byte)System.Math.Min(hp.Length, 255));
                        for (int i = 0; i < System.Math.Min(hp.Length, 255); i++) w.Write(hp[i]);
                        break;
                    case MessageType.GoldDelta:
                        w.Write(((GoldDelta)msg).Delta);
                        break;
                    case MessageType.GoldTotal:
                        w.Write(((GoldTotal)msg).Value);
                        break;
                    case MessageType.DayTimeSync:
                        var dts = (DayTimeSync)msg;
                        w.Write(dts.Value);
                        w.Write(dts.FromBed);
                        w.Write(dts.TotalDays);
                        break;
                    case MessageType.PoolSpent:
                        var ps = (PoolSpent)msg;
                        w.Write(ps.X); w.Write(ps.Y); w.Write(ps.Secret);
                        break;
                    case MessageType.BagSnapshot:
                        w.Write(((BagSnapshot)msg).Json ?? "");
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
                        case MessageType.SpriteDef:
                            return (type, new SpriteDef { Id = rd.ReadUInt16(), Key = rd.ReadString() });
                        case MessageType.Bye:
                            return (type, null);
                        case MessageType.Revive:
                        case MessageType.EmblemReturn:
                        case MessageType.CashSell:
                        case MessageType.ShopOpen:
                            return (type, null);
                        case MessageType.EntityStates:
                        {
                            var es = new EntityStates { Group = rd.ReadByte(), Extra = rd.ReadByte(), Key = rd.ReadInt32() };
                            es.Entities = new EntityState[rd.ReadUInt16()];
                            for (int i = 0; i < es.Entities.Length; i++)
                            {
                                var en = new EntityState { Id = rd.ReadUInt16(), X = rd.ReadSingle(), Y = rd.ReadSingle() };
                                en.Layers = new SpriteLayer[rd.ReadByte()];
                                for (int j = 0; j < en.Layers.Length; j++) en.Layers[j] = ReadLayer(rd);
                                es.Entities[i] = en;
                            }
                            return (type, es);
                        }
                        case MessageType.EnemyOwners:
                        {
                            var eo = new EnemyOwners { Key = rd.ReadInt32() };
                            eo.GuestOwned = new ushort[rd.ReadUInt16()];
                            for (int i = 0; i < eo.GuestOwned.Length; i++) eo.GuestOwned[i] = rd.ReadUInt16();
                            return (type, eo);
                        }
                        case MessageType.WorldSlot:
                            return (type, new WorldSlot { Data = rd.ReadBytes(rd.ReadInt32()) });
                        case MessageType.ShopState:
                            return (type, new ShopState { Data = rd.ReadBytes(rd.ReadInt32()) });
                        case MessageType.FloorLayout:
                            return (type, new FloorLayout { Culture = rd.ReadByte(), Floor = rd.ReadByte(), Json = rd.ReadString() });
                        case MessageType.DungeonTravel:
                            return (type, new DungeonTravel { Culture = rd.ReadByte(), Level = rd.ReadByte(), Replay = rd.ReadBoolean() });
                        case MessageType.TownTravel:
                            return (type, new TownTravel { FromEmblem = rd.ReadBoolean() });
                        case MessageType.RoomChange:
                            var rc = new RoomChange
                            {
                                FromX = rd.ReadInt16(), FromY = rd.ReadInt16(),
                                ToX = rd.ReadInt16(), ToY = rd.ReadInt16(),
                                DoorIndex = rd.ReadSByte(),
                            };
                            byte f = rd.ReadByte();
                            rc.Special = (f & 1) != 0;
                            rc.TeleportToLastDoor = (f & 2) != 0;
                            rc.FromSecret = (f & 4) != 0;
                            rc.ToSecret = (f & 8) != 0;
                            return (type, rc);
                        case MessageType.EnemyDamage:
                            return (type, new EnemyDamage { X = rd.ReadInt16(), Y = rd.ReadInt16(), Secret = rd.ReadBoolean(), Index = rd.ReadByte(), Delta = rd.ReadSingle() });
                        case MessageType.EnemyHealth:
                            var ehx = new EnemyHealth { X = rd.ReadInt16(), Y = rd.ReadInt16(), Secret = rd.ReadBoolean() };
                            int hn = rd.ReadByte();
                            ehx.Hp = new float[hn];
                            for (int i = 0; i < hn; i++) ehx.Hp[i] = rd.ReadSingle();
                            return (type, ehx);
                        case MessageType.GoldDelta:
                            return (type, new GoldDelta { Delta = rd.ReadInt32() });
                        case MessageType.GoldTotal:
                            return (type, new GoldTotal { Value = rd.ReadInt32() });
                        case MessageType.DayTimeSync:
                            return (type, new DayTimeSync { Value = rd.ReadByte(), FromBed = rd.ReadBoolean(), TotalDays = rd.ReadInt32() });
                        case MessageType.PoolSpent:
                            return (type, new PoolSpent { X = rd.ReadInt16(), Y = rd.ReadInt16(), Secret = rd.ReadBoolean() });
                        case MessageType.BagSnapshot:
                            return (type, new BagSnapshot { Json = rd.ReadString() });
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
            foreach (var l in layers) WriteLayer(w, l);
            w.Write(s.Hp);
            w.Write(s.MaxHp);
            w.Write(s.Ghost);
        }

        private static void WriteLayer(BinaryWriter w, SpriteLayer l)
        {
            w.Write(l.Slot);
            w.Write(l.SpriteId);
            byte flags = (byte)((l.Visible ? 1 : 0) | (l.FlipX ? 2 : 0));
            w.Write(flags);
            w.Write(l.SortingOrder);
            w.Write(l.LocalX);
            w.Write(l.LocalY);
        }

        private static SpriteLayer ReadLayer(BinaryReader rd)
        {
            var l = new SpriteLayer { Slot = rd.ReadByte(), SpriteId = rd.ReadUInt16() };
            byte flags = rd.ReadByte();
            l.Visible = (flags & 1) != 0;
            l.FlipX = (flags & 2) != 0;
            l.SortingOrder = rd.ReadInt16();
            l.LocalX = rd.ReadSingle();
            l.LocalY = rd.ReadSingle();
            return l;
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
            for (int i = 0; i < n; i++) s.Layers[i] = ReadLayer(rd);
            s.Hp = rd.ReadSingle();
            s.MaxHp = rd.ReadSingle();
            s.Ghost = rd.ReadBoolean();
            return s;
        }
    }
}
