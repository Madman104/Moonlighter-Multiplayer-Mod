using System;
using System.IO;
using Coop.Core.Protocol;
using Xunit;

namespace Coop.Core.Tests.Protocol
{
    public class FrameBufferTests
    {
        [Fact]
        public void Frame_split_across_three_appends_yields_one_payload()
        {
            var framed = FrameBuffer.Frame(new byte[] { 1, 2, 3, 4, 5, 6 });
            var buf = new FrameBuffer();
            buf.Append(framed, 0, 2);
            Assert.False(buf.TryReadFrame(out _));
            buf.Append(framed, 2, 4);
            Assert.False(buf.TryReadFrame(out _));
            buf.Append(framed, 6, framed.Length - 6);
            Assert.True(buf.TryReadFrame(out var payload));
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, payload);
            Assert.False(buf.TryReadFrame(out _));
        }

        [Fact]
        public void Three_frames_in_one_append_yield_three_payloads_in_order()
        {
            var a = FrameBuffer.Frame(new byte[] { 10 });
            var b = FrameBuffer.Frame(new byte[] { 20, 21 });
            var c = FrameBuffer.Frame(new byte[] { 30, 31, 32 });
            var all = new byte[a.Length + b.Length + c.Length];
            Buffer.BlockCopy(a, 0, all, 0, a.Length);
            Buffer.BlockCopy(b, 0, all, a.Length, b.Length);
            Buffer.BlockCopy(c, 0, all, a.Length + b.Length, c.Length);

            var buf = new FrameBuffer();
            buf.Append(all, 0, all.Length);
            Assert.True(buf.TryReadFrame(out var p1));
            Assert.True(buf.TryReadFrame(out var p2));
            Assert.True(buf.TryReadFrame(out var p3));
            Assert.False(buf.TryReadFrame(out _));
            Assert.Equal(new byte[] { 10 }, p1);
            Assert.Equal(new byte[] { 20, 21 }, p2);
            Assert.Equal(new byte[] { 30, 31, 32 }, p3);
        }

        [Fact]
        public void Empty_buffer_has_no_frame()
        {
            Assert.False(new FrameBuffer().TryReadFrame(out _));
        }

        [Fact]
        public void Length_prefix_over_limit_throws()
        {
            var buf = new FrameBuffer();
            var prefix = BitConverter.GetBytes(70000);
            buf.Append(prefix, 0, 4);
            Assert.Throws<InvalidDataException>(() => buf.TryReadFrame(out _));
        }

        [Fact]
        public void Frame_of_oversized_payload_throws()
        {
            Assert.Throws<InvalidDataException>(() => FrameBuffer.Frame(new byte[FrameBuffer.MaxFrameSize + 1]));
        }

        [Fact]
        public void Large_stream_of_frames_survives_byte_by_byte_feed()
        {
            var buf = new FrameBuffer();
            int received = 0;
            for (int i = 1; i <= 50; i++)
            {
                var payload = new byte[i * 37];
                payload[0] = (byte)i;
                var framed = FrameBuffer.Frame(payload);
                for (int k = 0; k < framed.Length; k++)
                {
                    buf.Append(framed, k, 1);
                    while (buf.TryReadFrame(out var p))
                    {
                        received++;
                        Assert.Equal(received * 37, p.Length);
                        Assert.Equal((byte)received, p[0]);
                    }
                }
            }
            Assert.Equal(50, received);
        }
    }
}
