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
    public class TcpTransportTests
    {
        private static int FreePort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        private static bool PollUntil(Func<bool> done, double seconds, params ITransport[] ts)
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < seconds)
            {
                foreach (var t in ts) t.Poll();
                if (done()) return true;
                Thread.Sleep(5);
            }
            return false;
        }

        [Fact]
        public void Host_and_client_connect_over_loopback()
        {
            int port = FreePort();
            using (var host = TcpTransport.Host(port))
            using (var client = TcpTransport.Connect("127.0.0.1", port))
            {
                bool hc = false, cc = false;
                host.Connected += () => hc = true;
                client.Connected += () => cc = true;
                Assert.True(PollUntil(() => hc && cc, 2, host, client));
                Assert.True(host.IsHost);
                Assert.False(client.IsHost);
            }
        }

        [Fact]
        public void Host_reports_loopback_peer()
        {
            int port = FreePort();
            using (var host = TcpTransport.Host(port))
            using (var client = TcpTransport.Connect("127.0.0.1", port))
            {
                bool hc = false;
                host.Connected += () => hc = true;
                Assert.False(host.RemoteIsLoopback);
                Assert.True(PollUntil(() => hc, 2, host, client));
                Assert.True(host.RemoteIsLoopback);
            }
        }

        [Fact]
        public void Hundred_payloads_arrive_complete_and_in_order()
        {
            int port = FreePort();
            using (var host = TcpTransport.Host(port))
            using (var client = TcpTransport.Connect("127.0.0.1", port))
            {
                var rnd = new Random(1);
                var sent = new List<byte[]>();
                for (int i = 0; i < 100; i++)
                {
                    var p = new byte[1 + rnd.Next(5000)];
                    rnd.NextBytes(p);
                    p[0] = (byte)i;
                    sent.Add(p);
                    client.Send(p, true); // queued before the connection completes
                }
                var got = new List<byte[]>();
                Assert.True(PollUntil(() =>
                {
                    while (host.TryReceive(out var p)) got.Add(p);
                    return got.Count == 100;
                }, 5, host, client));
                for (int i = 0; i < 100; i++) Assert.Equal(sent[i], got[i]);
            }
        }

        [Fact]
        public void Disposing_host_disconnects_client()
        {
            int port = FreePort();
            var host = TcpTransport.Host(port);
            using (var client = TcpTransport.Connect("127.0.0.1", port))
            {
                bool cc = false, hc = false;
                string reason = null;
                host.Connected += () => hc = true;
                client.Connected += () => cc = true;
                client.Disconnected += r => reason = r;
                Assert.True(PollUntil(() => cc && hc, 2, host, client));
                host.Dispose();
                Assert.True(PollUntil(() => reason != null, 2, client));
                Assert.Equal("Disconnected", reason);
            }
        }

        [Fact]
        public void Second_client_is_turned_away_while_first_stays()
        {
            int port = FreePort();
            using (var host = TcpTransport.Host(port))
            using (var first = TcpTransport.Connect("127.0.0.1", port))
            {
                bool firstUp = false, hostUp = false;
                string firstDown = null;
                first.Connected += () => firstUp = true;
                first.Disconnected += r => firstDown = r;
                host.Connected += () => hostUp = true;
                Assert.True(PollUntil(() => firstUp && hostUp, 2, host, first));

                using (var second = TcpTransport.Connect("127.0.0.1", port))
                {
                    string secondDown = null;
                    second.Disconnected += r => secondDown = r;
                    Assert.True(PollUntil(() => secondDown != null, 3, host, first, second));
                }

                first.Send(new byte[] { 42 }, true);
                byte[] got = null;
                Assert.True(PollUntil(() => host.TryReceive(out got), 2, host, first));
                Assert.Equal(new byte[] { 42 }, got);
                Assert.Null(firstDown);
            }
        }

        [Fact]
        public void Connecting_to_a_closed_port_reports_disconnected()
        {
            int port = FreePort();
            using (var client = TcpTransport.Connect("127.0.0.1", port))
            {
                string reason = null;
                client.Disconnected += r => reason = r;
                Assert.True(PollUntil(() => reason != null, 5, client));
                Assert.Contains("connect", reason, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
