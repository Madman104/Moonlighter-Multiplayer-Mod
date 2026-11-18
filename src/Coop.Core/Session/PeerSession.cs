using System;
using System.IO;
using Coop.Core.Net;
using Coop.Core.Protocol;

namespace Coop.Core.Session
{
    public enum SessionState
    {
        Idle,
        Handshaking,
        Connected,
        Closed,
    }

    /// <summary>
    /// One end of a co-op connection: runs the Hello/Welcome handshake, keep-alive pings,
    /// timeouts, and dispatches gameplay messages. Call Tick() once per frame.
    /// </summary>
    public sealed class PeerSession
    {
        public const double PingInterval = 1.0;
        public const double Timeout = 10.0;

        private readonly ITransport _transport;
        private readonly Hello _local;
        /// <summary>Host only: skip the DLC check (set for same-PC test connections).</summary>
        public bool IgnoreDlc { get; set; }
        private readonly Func<double> _clock;
        private double _lastReceive;
        private double _lastPing;
        private uint _pingNonce;

        public SessionState State { get; private set; } = SessionState.Idle;
        public string CloseReason { get; private set; }
        public int PlayerIndex { get; private set; } = -1;
        public bool IsHost => _transport.IsHost;

        public event Action<SessionState> StateChanged;
        public event Action<PlayerState> PlayerStateReceived;

        public PeerSession(ITransport transport, Hello local, bool ignoreDlc, Func<double> clock)
        {
            _transport = transport;
            _local = local;
            IgnoreDlc = ignoreDlc;
            _clock = clock;
            _transport.Connected += OnConnected;
            _transport.Disconnected += OnDisconnected;
        }

        public void Tick()
        {
            if (State == SessionState.Closed) return;
            _transport.Poll();

            while (State != SessionState.Closed && _transport.TryReceive(out var payload))
            {
                _lastReceive = _clock();
                MessageType type;
                object msg;
                try
                {
                    (type, msg) = MessageCodec.Decode(payload);
                }
                catch (InvalidDataException e)
                {
                    Close("Protocol error: " + e.Message);
                    return;
                }
                Handle(type, msg);
            }

            if (State == SessionState.Handshaking || State == SessionState.Connected)
            {
                double now = _clock();
                if (now - _lastPing >= PingInterval)
                {
                    _lastPing = now;
                    Send(MessageType.Ping, new Ping { Nonce = ++_pingNonce }, true);
                }
                if (now - _lastReceive > Timeout)
                    CloseQuietly("Timed out");
            }
        }

        public void SendPlayerState(PlayerState state)
        {
            if (State != SessionState.Connected) return;
            Send(MessageType.PlayerState, state, false);
        }

        /// <summary>Leaves the session and tells the other player.</summary>
        public void Close(string reason)
        {
            if (State == SessionState.Closed) return;
            Send(MessageType.Bye, null, true);
            CloseQuietly(reason);
        }

        private void OnConnected()
        {
            if (State != SessionState.Idle) return;
            double now = _clock();
            _lastReceive = now;
            _lastPing = now;
            SetState(SessionState.Handshaking);
            if (!_transport.IsHost)
                Send(MessageType.Hello, _local, true);
        }

        private void OnDisconnected(string reason)
        {
            CloseQuietly(string.IsNullOrEmpty(reason) ? "Disconnected" : reason);
        }

        private void Handle(MessageType type, object msg)
        {
            switch (type)
            {
                case MessageType.Hello when _transport.IsHost && State == SessionState.Handshaking:
                    var reject = HandshakeRules.Validate(_local, (Hello)msg, IgnoreDlc);
                    if (reject != null)
                    {
                        Send(MessageType.Reject, reject, true);
                        CloseQuietly("Rejected other player: " + reject.Detail);
                        return;
                    }
                    PlayerIndex = 0;
                    Send(MessageType.Welcome, new Welcome { PlayerIndex = 1 }, true);
                    SetState(SessionState.Connected);
                    break;
                case MessageType.Welcome when !_transport.IsHost && State == SessionState.Handshaking:
                    PlayerIndex = ((Welcome)msg).PlayerIndex;
                    SetState(SessionState.Connected);
                    break;
                case MessageType.Reject when !_transport.IsHost:
                    CloseQuietly(((Reject)msg).Detail);
                    break;
                case MessageType.Ping:
                    Send(MessageType.Pong, msg, true);
                    break;
                case MessageType.Bye:
                    CloseQuietly("Other player left");
                    break;
                case MessageType.PlayerState when State == SessionState.Connected:
                    PlayerStateReceived?.Invoke((PlayerState)msg);
                    break;
                // Pong only refreshes _lastReceive; anything out of order is ignored.
            }
        }

        private void Send(MessageType type, object msg, bool reliable)
        {
            _transport.Send(MessageCodec.Encode(type, msg), reliable);
        }

        private void CloseQuietly(string reason)
        {
            if (State == SessionState.Closed) return;
            CloseReason = reason;
            SetState(SessionState.Closed);
        }

        private void SetState(SessionState s)
        {
            State = s;
            StateChanged?.Invoke(s);
        }
    }
}
