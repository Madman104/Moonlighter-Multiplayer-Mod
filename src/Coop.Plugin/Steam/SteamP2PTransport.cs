using System;
using System.Collections.Generic;
using Coop.Core.Net;
using Steamworks;

namespace Coop.Plugin.Steam
{
    /// <summary>
    /// Reads Steam's P2P packets once per frame and hands each to the transport of the player
    /// who sent it. (Steam has one incoming queue for everyone, so with several guests each
    /// transport must not read it by itself.)
    /// </summary>
    internal static class SteamP2PRouter
    {
        private const int Channel = 0;
        private static readonly Dictionary<CSteamID, SteamP2PTransport> Routes = new Dictionary<CSteamID, SteamP2PTransport>();
        private static int _lastFrame = -1;

        public static void Register(CSteamID who, SteamP2PTransport t) => Routes[who] = t;

        public static void Unregister(CSteamID who, SteamP2PTransport t)
        {
            if (Routes.TryGetValue(who, out var cur) && cur == t) Routes.Remove(who);
        }

        public static bool Knows(CSteamID who) => Routes.ContainsKey(who);

        /// <summary>Called by every transport's Poll; only the first call per frame reads.</summary>
        public static void Pump()
        {
            int frame = UnityEngine.Time.frameCount;
            if (frame == _lastFrame) return;
            _lastFrame = frame;
            while (SteamNetworking.IsP2PPacketAvailable(out uint size, Channel))
            {
                var buf = new byte[size];
                if (!SteamNetworking.ReadP2PPacket(buf, size, out uint read, out CSteamID from, Channel)) break;
                if (read != size) Array.Resize(ref buf, (int)read);
                if (Routes.TryGetValue(from, out var t)) t.Deliver(buf); // anyone else is ignored
            }
        }
    }

    /// <summary>
    /// Transport to one other player over Steam's P2P networking. Steam relays the traffic when
    /// a direct connection isn't possible, so nobody needs to forward ports.
    /// Each Steam packet is one message, so no framing is needed.
    /// </summary>
    internal sealed class SteamP2PTransport : ITransport
    {
        private const int Channel = 0;
        private const int MaxUnreliableBytes = 1200;
        private bool _loggedSendFailure;

        private readonly Queue<byte[]> _inbox = new Queue<byte[]>();
        private CSteamID _remote = CSteamID.Nil;
        private bool _pendingConnected;
        private string _pendingDisconnect;
        private bool _closed;
        private Callback<P2PSessionRequest_t> _sessionRequest;
        private Callback<P2PSessionConnectFail_t> _connectFail;

        public bool IsHost { get; }
        public CSteamID Remote => _remote;

        public event Action Connected;
        public event Action<string> Disconnected;

        public SteamP2PTransport(bool isHost)
        {
            IsHost = isHost;
            _sessionRequest = Callback<P2PSessionRequest_t>.Create(OnSessionRequest);
            _connectFail = Callback<P2PSessionConnectFail_t>.Create(OnConnectFail);
        }

        /// <summary>The other player is known (joined the lobby / owns the lobby).</summary>
        public void SetRemote(CSteamID remote)
        {
            if (_closed || _remote != CSteamID.Nil) return;
            _remote = remote;
            SteamP2PRouter.Register(remote, this);
            SteamNetworking.AcceptP2PSessionWithUser(remote);
            _pendingConnected = true;
        }

        /// <summary>Raised by the lobby when the other player leaves it.</summary>
        public void NotifyRemoteLeft(string reason)
        {
            if (_closed) return;
            _pendingDisconnect = reason;
        }

        internal void Deliver(byte[] packet)
        {
            if (!_closed) _inbox.Enqueue(packet);
        }

        public void Send(byte[] payload, bool reliable)
        {
            if (_closed || _remote == CSteamID.Nil) return;
            // Steam drops unreliable packets over 1200 bytes, so big ones go reliable instead.
            if (!reliable && payload.Length > MaxUnreliableBytes) reliable = true;
            var type = reliable ? EP2PSend.k_EP2PSendReliable : EP2PSend.k_EP2PSendUnreliableNoDelay;
            if (!SteamNetworking.SendP2PPacket(_remote, payload, (uint)payload.Length, type, Channel) && !_loggedSendFailure)
            {
                _loggedSendFailure = true;
                CoopPlugin.Log.LogWarning($"Steam refused a {payload.Length}-byte packet (reliable={reliable})");
            }
        }

        public bool TryReceive(out byte[] payload)
        {
            if (_inbox.Count > 0) { payload = _inbox.Dequeue(); return true; }
            payload = null;
            return false;
        }

        public void Poll()
        {
            if (_closed) return;
            if (_pendingConnected) { _pendingConnected = false; Connected?.Invoke(); }
            SteamP2PRouter.Pump();
            if (_pendingDisconnect != null)
            {
                var r = _pendingDisconnect;
                _pendingDisconnect = null;
                Close();
                Disconnected?.Invoke(r);
            }
        }

        private void OnSessionRequest(P2PSessionRequest_t req)
        {
            if (!_closed && req.m_steamIDRemote == _remote)
                SteamNetworking.AcceptP2PSessionWithUser(req.m_steamIDRemote);
        }

        private void OnConnectFail(P2PSessionConnectFail_t fail)
        {
            if (fail.m_steamIDRemote == _remote)
                NotifyRemoteLeft("Steam connection failed (error " + fail.m_eP2PSessionError + ")");
        }

        private void Close()
        {
            if (_closed) return;
            _closed = true;
            if (_remote != CSteamID.Nil)
            {
                SteamP2PRouter.Unregister(_remote, this);
                SteamNetworking.CloseP2PSessionWithUser(_remote);
            }
            _sessionRequest?.Dispose();
            _connectFail?.Dispose();
            _sessionRequest = null;
            _connectFail = null;
        }

        public void Dispose() => Close();
    }

    /// <summary>Host: one transport per player who joins the Steam lobby (up to three).</summary>
    internal sealed class SteamHostListener : IGuestListener
    {
        private readonly Queue<ITransport> _pending = new Queue<ITransport>();
        private readonly Dictionary<CSteamID, SteamP2PTransport> _guests = new Dictionary<CSteamID, SteamP2PTransport>();
        private bool _closed;

        public event Action Closed;

        public void Joined(CSteamID who)
        {
            if (_closed || (_guests.TryGetValue(who, out var old) && old != null && SteamP2PRouter.Knows(who))) return;
            var t = new SteamP2PTransport(isHost: true);
            t.SetRemote(who);
            _guests[who] = t;
            _pending.Enqueue(t);
        }

        public void Left(CSteamID who, string reason)
        {
            if (_guests.TryGetValue(who, out var t)) t.NotifyRemoteLeft(reason);
            _guests.Remove(who);
        }

        public void Poll() => SteamP2PRouter.Pump();

        public bool TryAccept(out ITransport transport)
        {
            if (_pending.Count > 0) { transport = _pending.Dequeue(); return true; }
            transport = null;
            return false;
        }

        public void Dispose()
        {
            if (_closed) return;
            _closed = true;
            foreach (var t in _guests.Values) t.Dispose();
            _guests.Clear();
            Closed?.Invoke();
        }
    }
}
