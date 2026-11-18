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
                    case MessageType.CatchUp:
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
                    case MessageType.PlayerName:
                        w.Write(((PlayerName)msg).Name ?? "");
                        break;
                    case MessageType.Relay:
                        var rl = (Relay)msg;
                        w.Write(rl.From);
                        w.Write((rl.Inner ?? new byte[0]).Length);
                        w.Write(rl.Inner ?? new byte[0]);
                        break;
                    case MessageType.EnemyOwners:
                        var eo = (EnemyOwners)msg;
                        var ids = eo.Ids ?? new ushort[0];
                        var owners = eo.Owners ?? new byte[0];
                        int count = Math.Min(Math.Min(ids.Length, owners.Length), ushort.MaxValue);
                        w.Write(eo.Key);
                        w.Write((ushort)count);
                        for (int i = 0; i < count; i++) { w.Write(ids[i]); w.Write(owners[i]); }
                        break;
                    case MessageType.WorldSlot:
                    case MessageType.ShopState:
                    case MessageType.TownState:
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
                        for (int i = 0; i < System.Math.Min(hp.Length, 255); i++) w.Write(eh.Max != null && i < eh.Max.Length ? eh.Max[i] : -1f);
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
                    case MessageType.PlayerSound:
                        var snd = (PlayerSound)msg;
                        var sn = snd.Name ?? "";
                        w.Write(sn.Length > PlayerSound.MaxNameLength ? sn.Substring(0, PlayerSound.MaxNameLength) : sn);
                        w.Write(snd.Volume);
                        w.Write(snd.Stop);
                        break;
                    case MessageType.FloorItemSpawn:
                        var fis = (FloorItemSpawn)msg;
                        w.Write(fis.Id); w.Write(fis.RoomKey); w.Write(fis.X); w.Write(fis.Y); w.Write(fis.Json ?? "");
                        break;
                    case MessageType.FloorItemTaken:
                        var fit = (FloorItemTaken)msg;
                        w.Write(fit.Id); w.Write(fit.Remaining);
                        break;
                    case MessageType.ChestUse:
                        var cu = (ChestUse)msg;
                        w.Write(cu.RoomKey); w.Write(cu.Index); w.Write(cu.Open);
                        break;
                    case MessageType.ChestContents:
                        var cc = (ChestContents)msg;
                        w.Write(cc.RoomKey); w.Write(cc.Index); w.Write(cc.Json ?? "");
                        break;
                    case MessageType.BreakableBroken:
                        var bb = (BreakableBroken)msg;
                        w.Write(bb.RoomKey); w.Write(bb.X); w.Write(bb.Y);
                        break;
                    case MessageType.RoomObject:
                        var ro = (RoomObject)msg;
                        w.Write(ro.RoomKey); w.Write(ro.Kind); w.Write(ro.X); w.Write(ro.Y); w.Write(ro.Value);
                        break;
                    case MessageType.PartnerHit:
                        var ph = (PartnerHit)msg;
                        w.Write(ph.Target); w.Write(ph.Damage); w.Write(ph.Source ?? "");
                        break;
                    case MessageType.SummonSpawned:
                        var ss = (SummonSpawned)msg;
                        w.Write(ss.RoomKey); w.Write(ss.Id); w.Write(ss.Name ?? ""); w.Write(ss.Tile); w.Write(ss.X); w.Write(ss.Y);
                        break;
                    case MessageType.SummonGone:
                        var sg = (SummonGone)msg;
                        w.Write(sg.RoomKey); w.Write(sg.Id);
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
                        case MessageType.CatchUp:
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
                        case MessageType.PlayerName:
                            return (type, new PlayerName { Name = rd.ReadString() });
                        case MessageType.PlayerSound:
                            return (type, new PlayerSound { Name = rd.ReadString(), Volume = rd.ReadSingle(), Stop = rd.ReadBoolean() });
                        case MessageType.FloorItemSpawn:
                            return (type, new FloorItemSpawn { Id = rd.ReadInt32(), RoomKey = rd.ReadInt32(), X = rd.ReadSingle(), Y = rd.ReadSingle(), Json = rd.ReadString() });
                        case MessageType.FloorItemTaken:
                            return (type, new FloorItemTaken { Id = rd.ReadInt32(), Remaining = rd.ReadInt32() });
                        case MessageType.ChestUse:
                            return (type, new ChestUse { RoomKey = rd.ReadInt32(), Index = rd.ReadByte(), Open = rd.ReadBoolean() });
                        case MessageType.ChestContents:
                            return (type, new ChestContents { RoomKey = rd.ReadInt32(), Index = rd.ReadByte(), Json = rd.ReadString() });
                        case MessageType.BreakableBroken:
                            return (type, new BreakableBroken { RoomKey = rd.ReadInt32(), X = rd.ReadSingle(), Y = rd.ReadSingle() });
                        case MessageType.RoomObject:
                            return (type, new RoomObject { RoomKey = rd.ReadInt32(), Kind = rd.ReadByte(), X = rd.ReadSingle(), Y = rd.ReadSingle(), Value = rd.ReadSingle() });
                        case MessageType.PartnerHit:
                            return (type, new PartnerHit { Target = rd.ReadByte(), Damage = rd.ReadSingle(), Source = rd.ReadString() });
                        case MessageType.SummonSpawned:
                            return (type, new SummonSpawned { RoomKey = rd.ReadInt32(), Id = rd.ReadByte(), Name = rd.ReadString(), Tile = rd.ReadInt32(), X = rd.ReadSingle(), Y = rd.ReadSingle() });
                        case MessageType.SummonGone:
                            return (type, new SummonGone { RoomKey = rd.ReadInt32(), Id = rd.ReadByte() });
                        case MessageType.Relay:
                        {
                            var rl = new Relay { From = rd.ReadByte() };
                            int rn = rd.ReadInt32();
                            if (rn < 0 || rn > FrameBuffer.MaxFrameSize) throw new InvalidDataException("bad relay size");
                            rl.Inner = rd.ReadBytes(rn);
                            return (type, rl);
                        }
                        case MessageType.EnemyOwners:
                        {
                            var eo = new EnemyOwners { Key = rd.ReadInt32() };
                            int count = rd.ReadUInt16();
                            eo.Ids = new ushort[count];
                            eo.Owners = new byte[count];
                            for (int i = 0; i < count; i++) { eo.Ids[i] = rd.ReadUInt16(); eo.Owners[i] = rd.ReadByte(); }
                            return (type, eo);
                        }
                        case MessageType.WorldSlot:
                            return (type, new WorldSlot { Data = rd.ReadBytes(rd.ReadInt32()) });
                        case MessageType.ShopState:
                            return (type, new ShopState { Data = rd.ReadBytes(rd.ReadInt32()) });
                        case MessageType.TownState:
                            return (type, new TownState { Data = rd.ReadBytes(rd.ReadInt32()) });
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
                            ehx.Max = new float[hn];
                            for (int i = 0; i < hn; i++) ehx.Max[i] = rd.ReadSingle();
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
            w.Write(s.RoomKey);
        }

        private static void WriteLayer(BinaryWriter w, SpriteLayer l)
        {
            w.Write(l.Slot);
            w.Write(l.SpriteId);
            byte flags = (byte)((l.Visible ? 1 : 0) | (l.FlipX ? 2 : 0));
            w.Write(flags);
            w.Write(l.SortingOrder);
            w.Write(l.SortingLayerId);
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
            l.SortingLayerId = rd.ReadInt32();
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
            s.RoomKey = rd.ReadInt32();
            return s;
        }
    }
}
