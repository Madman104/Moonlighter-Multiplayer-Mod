using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Coop.Core.Net;
using Xunit;

namespace Coop.Core.Tests.Net
{
    public class TcpGuestListenerTests
    {
        private static int FreePort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        [Fact]
        public void Accepts_two_guests_and_both_can_talk()
        {
            int port = FreePort();
            using (var listener = new TcpGuestListener(port))
            using (var a = TcpTransport.Connect("127.0.0.1", port))
            using (var b = TcpTransport.Connect("127.0.0.1", port))
            {
                var hosts = new List<ITransport>();
                var sw = Stopwatch.StartNew();
                while (hosts.Count < 2 && sw.Elapsed.TotalSeconds < 3)
                {
                    a.Poll(); b.Poll(); listener.Poll();
                    while (listener.TryAccept(out var t)) hosts.Add(t);
                    Thread.Sleep(5);
                }
                Assert.Equal(2, hosts.Count);
                foreach (var h in hosts) { h.Poll(); Assert.True(h.IsHost); }
                a.Send(new byte[] { 1 }, true);
                b.Send(new byte[] { 2 }, true);
                var got = new List<byte>();
                sw.Restart();
                while (got.Count < 2 && sw.Elapsed.TotalSeconds < 3)
                {
                    a.Poll(); b.Poll();
                    foreach (var h in hosts) { h.Poll(); while (h.TryReceive(out var p)) got.Add(p[0]); }
                    Thread.Sleep(5);
                }
                Assert.Contains((byte)1, got);
                Assert.Contains((byte)2, got);
                foreach (var h in hosts) h.Dispose();
            }
        }
    }
}
