using System;
using System.Collections.Generic;
using System.Linq;
using Coop.Core.Net;
using Coop.Core.Protocol;

namespace Coop.Core.Session
{
    /// <summary>
    /// The co-op link for up to four players (host + three guests).
    ///
    /// Host: one PeerSession per guest. Sends go to every guest (or one, with SendGameTo).
    /// Anything a guest sends is delivered here with LastFrom = that guest's number, and is
    /// forwarded to the other guests (except host-only requests), so every game hears every
    /// player. Guests: one PeerSession to the host; forwarded messages arrive with LastFrom =
    /// the original sender. Player numbers: host 0, guests 1..3 (given by the host).
    /// </summary>
    public sealed class CoopLink
    {
        public const int MaxPlayers = 4;
        public const double LingerSeconds = 1.0;

        private readonly Hello _hello;
        private readonly Func<double> _clock;
        private readonly bool _ignoreDlc;
        private readonly IGuestListener _listener;
        private readonly Dictionary<int, PeerSession> _peers = new Dictionary<int, PeerSession>();
        private readonly Dictionary<int, ITransport> _transports = new Dictionary<int, ITransport>();
        private readonly HashSet<int> _connected = new HashSet<int>();
        private readonly Dictionary<int, PlayerName> _names = new Dictionary<int, PlayerName>();
        private readonly List<(ITransport t, double until)> _lingering = new List<(ITransport, double)>();

        public bool IsHost { get; }
        public int LocalIndex { get; private set; }
        public SessionState State { get; private set; } = SessionState.Idle;
        public string CloseReason { get; private set; }

        /// <summary>Player number of whoever sent the message being delivered right now.</summary>
        public int LastFrom { get; private set; } = -1;

        /// <summary>Connected remote players (host: guests; guest: the host, 0).</summary>
        public IEnumerable<int> Peers => _connected;
        public int PeerCount => _connected.Count;

        /// <summary>Host only: decides per new guest transport whether to skip the DLC check.</summary>
        public Func<ITransport, bool> IgnoreDlcFor;

        public event Action<SessionState> StateChanged;
        public event Action<int> PeerJoined;
        public event Action<int> PeerLeft;
        public event Action<PlayerState> PlayerStateReceived;
        public event Action<SpriteDef> SpriteDefReceived;
        public event Action<MessageType, object> GameMessageReceived;

        private CoopLink(bool isHost, IGuestListener listener, Hello hello, bool ignoreDlc, Func<double> clock)
        {
            IsHost = isHost;
            _listener = listener;
            _hello = hello;
            _ignoreDlc = ignoreDlc;
            _clock = clock;
            LocalIndex = isHost ? 0 : -1;
        }

        public static CoopLink Host(IGuestListener listener, Hello hello, bool ignoreDlc, Func<double> clock)
        {
            var link = new CoopLink(true, listener, hello, ignoreDlc, clock);
            link.SetState(SessionState.Handshaking); // waiting for players
            return link;
        }

        public static CoopLink Guest(ITransport transport, Hello hello, bool ignoreDlc, Func<double> clock)
        {
            var link = new CoopLink(false, null, hello, ignoreDlc, clock);
            link.AddSession(0, transport);
            return link;
        }

        /// <summary>Messages that only mean something to the host; never forwarded to guests.</summary>
        public static bool HostOnly(MessageType t)
            => t == MessageType.CatchUp || t == MessageType.EmblemReturn || t == MessageType.CashSell
            || t == MessageType.Relay || t == MessageType.WorldSlot;

        private void AddSession(int index, ITransport transport)
        {
            bool ignoreDlc = _ignoreDlc || (IgnoreDlcFor != null && IgnoreDlcFor(transport));
            var s = new PeerSession(transport, _hello, ignoreDlc, _clock) { AssignedIndex = (byte)Math.Max(1, index) };
            _peers[index] = s;
            _transports[index] = transport;
            s.StateChanged += st => OnPeerState(index, s, st);
            s.PlayerStateReceived += st => Deliver(index, MessageType.PlayerState, st);
            s.SpriteDefReceived += d => Deliver(index, MessageType.SpriteDef, d);
            s.GameMessageReceived += (t, m) => Deliver(index, t, m);
        }

        private void OnPeerState(int index, PeerSession s, SessionState st)
        {
            if (!IsHost)
            {
                if (st == SessionState.Connected) { LocalIndex = s.PlayerIndex; _connected.Add(0); }
                if (st == SessionState.Closed)
                {
                    _connected.Remove(0);
                    CloseReason = s.CloseReason;
                    // Keep the link open briefly so a final Reject/Bye still gets through.
                    if (_transports.TryGetValue(0, out var gt)) _lingering.Add((gt, _clock() + LingerSeconds));
                    _transports.Clear();
                    _peers.Clear();
                }
                SetState(st);
                return;
            }
            if (st == SessionState.Connected)
            {
                _connected.Add(index);
                // Tell the newcomer who is already here.
                foreach (var kv in _names)
                    if (kv.Key != index && _connected.Contains(kv.Key))
                        s.SendGame(MessageType.Relay, Wrap(kv.Key, MessageType.PlayerName, kv.Value));
                SetState(SessionState.Connected);
                PeerJoined?.Invoke(index);
            }
            else if (st == SessionState.Closed)
            {
                bool was = _connected.Remove(index);
                _names.Remove(index);
                _peers.Remove(index);
                if (_transports.TryGetValue(index, out var t)) _lingering.Add((t, _clock() + LingerSeconds));
                _transports.Remove(index);
                if (was) PeerLeft?.Invoke(index);
                if (_connected.Count == 0 && State == SessionState.Connected) SetState(SessionState.Handshaking);
            }
        }

        private static Relay Wrap(int from, MessageType type, object msg)
            => new Relay { From = (byte)from, Inner = MessageCodec.Encode(type, msg) };

        private void Deliver(int index, MessageType type, object msg)
        {
            if (!IsHost && type == MessageType.Relay)
            {
                var r = (Relay)msg;
                MessageType inner;
                object innerMsg;
                try { (inner, innerMsg) = MessageCodec.Decode(r.Inner); }
                catch (System.IO.InvalidDataException) { return; }
                Raise(r.From, inner, innerMsg);
                return;
            }
            if (IsHost && type == MessageType.Relay) return; // guests don't relay
            if (IsHost && type == MessageType.PlayerName) _names[index] = (PlayerName)msg;
            Raise(index, type, msg);
            if (IsHost && !HostOnly(type)) Forward(index, type, msg);
        }

        private void Raise(int from, MessageType type, object msg)
        {
            int prev = LastFrom;
            LastFrom = from;
            try
            {
                switch (type)
                {
                    case MessageType.PlayerState: PlayerStateReceived?.Invoke((PlayerState)msg); break;
                    case MessageType.SpriteDef: SpriteDefReceived?.Invoke((SpriteDef)msg); break;
                    default: GameMessageReceived?.Invoke(type, msg); break;
                }
            }
            finally { LastFrom = prev; }
        }

        private void Forward(int from, MessageType type, object msg)
        {
            if (_connected.Count < 2) return;
            var wrapped = Wrap(from, type, msg);
            foreach (var i in _connected)
            {
                if (i == from || !_peers.TryGetValue(i, out var s)) continue;
                if (type == MessageType.PlayerState) s.SendFrequent(MessageType.Relay, wrapped);
                else s.SendGame(MessageType.Relay, wrapped);
            }
        }

        public void Tick()
        {
            if (State != SessionState.Closed)
            {
                if (IsHost) AcceptGuests();
                foreach (var s in _peers.Values.ToArray()) s.Tick();
            }
            double now = _clock();
            for (int i = _lingering.Count - 1; i >= 0; i--)
            {
                try { _lingering[i].t.Poll(); } catch (Exception) { /* closing anyway */ }
                if (now >= _lingering[i].until)
                {
                    try { _lingering[i].t.Dispose(); } catch (Exception) { }
                    _lingering.RemoveAt(i);
                }
            }
        }

        private void AcceptGuests()
        {
            _listener.Poll();
            while (_listener.TryAccept(out var t))
            {
                int index = 0;
                for (int i = 1; i < MaxPlayers; i++) if (!_peers.ContainsKey(i)) { index = i; break; }
                if (index == 0) { t.Dispose(); continue; } // full
                AddSession(index, t);
            }
        }

        // ---- sending ----

        public void SendGame(MessageType type, object msg)
        {
            foreach (var s in ConnectedSessions()) s.SendGame(type, msg);
        }

        public void SendGameTo(int player, MessageType type, object msg)
        {
            if (IsHost) { if (_connected.Contains(player) && _peers.TryGetValue(player, out var s)) s.SendGame(type, msg); }
            else if (player == 0) SendGame(type, msg);
        }

        public void SendFrequent(MessageType type, object msg)
        {
            foreach (var s in ConnectedSessions()) s.SendFrequent(type, msg);
        }

        public void SendSpriteDef(SpriteDef def)
        {
            foreach (var s in ConnectedSessions()) s.SendSpriteDef(def);
        }

        public void SendPlayerState(PlayerState state)
        {
            foreach (var s in ConnectedSessions()) s.SendPlayerState(state);
        }

        private IEnumerable<PeerSession> ConnectedSessions()
        {
            foreach (var i in _connected.ToArray())
                if (_peers.TryGetValue(i, out var s)) yield return s;
        }

        /// <summary>Leave (guest) or stop hosting (host): tell everyone and close.</summary>
        public void Close(string reason)
        {
            if (State == SessionState.Closed) return;
            foreach (var s in _peers.Values.ToArray()) s.Close(reason);
            foreach (var t in _transports.Values) { try { t.Dispose(); } catch (Exception) { } }
            _transports.Clear();
            _peers.Clear();
            _connected.Clear();
            foreach (var (t, _) in _lingering) { try { t.Dispose(); } catch (Exception) { } }
            _lingering.Clear();
            _listener?.Dispose();
            CloseReason = reason;
            SetState(SessionState.Closed);
        }

        private void SetState(SessionState st)
        {
            if (State == st) return;
            State = st;
            StateChanged?.Invoke(st);
        }
    }
}
