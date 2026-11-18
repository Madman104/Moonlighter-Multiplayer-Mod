using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Coop.Core.Protocol;

namespace Coop.Core.Net
{
    /// <summary>
    /// Direct-IP transport (local test mode and the port-forwarding fallback).
    /// Non-blocking sockets, no threads: everything happens in Poll().
    /// The host accepts exactly one client; extra connections are closed at once.
    /// </summary>
    public sealed class TcpTransport : ITransport
    {
        public const double ConnectTimeoutSeconds = 10;

        private readonly Socket _listener;
        private Socket _socket;
        private readonly string _target;
        private readonly Stopwatch _connectTimer;
        private bool _connecting;
        private bool _connected;
        private bool _closed;
        private readonly FrameBuffer _frames = new FrameBuffer();
        private readonly Queue<byte[]> _inbox = new Queue<byte[]>();
        private readonly byte[] _readBuf = new byte[16384];
        private byte[] _out = new byte[0];
        private int _outOffset;

        public bool IsHost { get; }

        /// <summary>True once connected to a peer on this same PC (127.0.0.1).</summary>
        public bool RemoteIsLoopback { get; private set; }

        public event Action Connected;
        public event Action<string> Disconnected;

        private TcpTransport(bool isHost, Socket listener, Socket socket, string target)
        {
            IsHost = isHost;
            _listener = listener;
            _socket = socket;
            _target = target;
            if (!isHost)
            {
                _connecting = true;
                _connectTimer = Stopwatch.StartNew();
            }
        }

        public static TcpTransport Host(int port)
        {
            var l = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            l.Bind(new IPEndPoint(IPAddress.Any, port));
            l.Listen(4);
            l.Blocking = false;
            return new TcpTransport(true, l, null, null);
        }

        public static TcpTransport Connect(string host, int port)
        {
            var s = NewSocket();
            var address = ResolveIPv4(host);
            try
            {
                s.Connect(new IPEndPoint(address, port));
            }
            catch (SocketException e) when (e.SocketErrorCode == SocketError.WouldBlock || e.SocketErrorCode == SocketError.InProgress)
            {
                // Expected for a non-blocking connect; completion is checked in Poll().
            }
            return new TcpTransport(false, null, s, host + ":" + port);
        }

        public void Send(byte[] payload, bool reliable)
        {
            if (_closed) return;
            var framed = FrameBuffer.Frame(payload);
            int pending = _out.Length - _outOffset;
            var merged = new byte[pending + framed.Length];
            Buffer.BlockCopy(_out, _outOffset, merged, 0, pending);
            Buffer.BlockCopy(framed, 0, merged, pending, framed.Length);
            _out = merged;
            _outOffset = 0;
            if (_connected) Flush();
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
            if (IsHost) AcceptPending();
            if (_connecting) CheckConnect();
            if (!_connected) return;
            Flush();
            Read();
        }

        private void AcceptPending()
        {
            while (true)
            {
                Socket s;
                try { s = _listener.Accept(); }
                catch (SocketException) { return; }

                if (_socket != null)
                {
                    // Only two players: turn extra connections away.
                    try { s.Shutdown(SocketShutdown.Both); } catch (SocketException) { }
                    s.Close();
                    continue;
                }
                s.Blocking = false;
                s.NoDelay = true;
                _socket = s;
                RemoteIsLoopback = s.RemoteEndPoint is IPEndPoint ep && IPAddress.IsLoopback(ep.Address);
                _connected = true;
                Connected?.Invoke();
            }
        }

        private void CheckConnect()
        {
            if (_socket.Poll(0, SelectMode.SelectError))
            {
                Fail("Could not connect to " + _target);
                return;
            }
            if (_socket.Poll(0, SelectMode.SelectWrite))
            {
                _connecting = false;
                _connected = true;
                Connected?.Invoke();
                return;
            }
            if (_connectTimer.Elapsed.TotalSeconds > ConnectTimeoutSeconds)
                Fail("Could not connect to " + _target + " (timed out)");
        }

        private void Flush()
        {
            while (_outOffset < _out.Length)
            {
                int n = _socket.Send(_out, _outOffset, _out.Length - _outOffset, SocketFlags.None, out var err);
                if (err == SocketError.WouldBlock) return;
                if (err != SocketError.Success) { Fail("Disconnected"); return; }
                _outOffset += n;
            }
            _out = new byte[0];
            _outOffset = 0;
        }

        private void Read()
        {
            while (!_closed)
            {
                int n = _socket.Receive(_readBuf, 0, _readBuf.Length, SocketFlags.None, out var err);
                if (err == SocketError.WouldBlock) return;
                if (err != SocketError.Success || n == 0) { Fail("Disconnected"); return; }
                _frames.Append(_readBuf, 0, n);
                try
                {
                    while (_frames.TryReadFrame(out var payload)) _inbox.Enqueue(payload);
                }
                catch (System.IO.InvalidDataException)
                {
                    Fail("Disconnected (bad data from other player)");
                    return;
                }
            }
        }

        private void Fail(string reason)
        {
            if (_closed) return;
            CloseSockets();
            Disconnected?.Invoke(reason);
        }

        private void CloseSockets()
        {
            _closed = true;
            _connected = false;
            _connecting = false;
            if (_socket != null) { try { _socket.Close(); } catch (SocketException) { } }
            if (_listener != null) { try { _listener.Close(); } catch (SocketException) { } }
        }

        public void Dispose()
        {
            if (_closed) return;
            if (_connected) { try { Flush(); } catch (SocketException) { } catch (ObjectDisposedException) { } }
            CloseSockets();
        }

        private static Socket NewSocket()
        {
            return new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
            {
                Blocking = false,
                NoDelay = true,
            };
        }

        private static IPAddress ResolveIPv4(string host)
        {
            if (IPAddress.TryParse(host, out var ip)) return ip;
            foreach (var a in Dns.GetHostAddresses(host))
                if (a.AddressFamily == AddressFamily.InterNetwork) return a;
            throw new ArgumentException("No IPv4 address for " + host);
        }
    }
}
