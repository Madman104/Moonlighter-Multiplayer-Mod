using System;
using System.Collections.Generic;
using Coop.Core.Net;

namespace Coop.Core.Tests.Session
{
    /// <summary>In-memory transport pair for session tests. Delivers on the receiver's Poll().</summary>
    public sealed class FakeTransport : ITransport
    {
        private readonly Queue<byte[]> _inbox = new Queue<byte[]>();
        private FakeTransport _peer;
        private bool _pendingConnected;
        private string _pendingDisconnect;

        public bool IsHost { get; private set; }
        public int SentCount;
        public bool LastSendReliable;
        public bool Disposed;

        public event Action Connected;
        public event Action<string> Disconnected;

        public static (FakeTransport host, FakeTransport client) CreatePair()
        {
            var h = new FakeTransport { IsHost = true };
            var c = new FakeTransport { IsHost = false };
            h._peer = c;
            c._peer = h;
            return (h, c);
        }

        /// <summary>Both sides raise Connected on their next Poll.</summary>
        public void ConnectBoth()
        {
            _pendingConnected = true;
            _peer._pendingConnected = true;
        }

        /// <summary>Simulates the link dropping: both sides raise Disconnected on next Poll.</summary>
        public void DropLink(string reason)
        {
            _pendingDisconnect = reason;
            _peer._pendingDisconnect = reason;
        }

        /// <summary>Drops this many upcoming sends (simulates lost packets).</summary>
        public int DropNextSends;

        public void Send(byte[] payload, bool reliable)
        {
            SentCount++;
            LastSendReliable = reliable;
            if (DropNextSends > 0) { DropNextSends--; return; }
            if (_peer != null && !_peer.Disposed) _peer._inbox.Enqueue(payload);
        }

        public bool TryReceive(out byte[] payload)
        {
            if (_inbox.Count > 0) { payload = _inbox.Dequeue(); return true; }
            payload = null;
            return false;
        }

        public void Poll()
        {
            if (_pendingConnected) { _pendingConnected = false; Connected?.Invoke(); }
            if (_pendingDisconnect != null) { var r = _pendingDisconnect; _pendingDisconnect = null; Disconnected?.Invoke(r); }
        }

        public void Dispose() { Disposed = true; }
    }
}
