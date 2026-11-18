using System;
using System.IO;

namespace Coop.Core.Protocol
{
    /// <summary>
    /// Length-prefixed framing for stream transports (TCP): 4-byte little-endian length,
    /// then the payload. Handles partial and coalesced reads.
    /// </summary>
    public sealed class FrameBuffer
    {
        public const int MaxFrameSize = 1 << 20;

        private byte[] _buf = new byte[4096];
        private int _start;
        private int _end;

        public static byte[] Frame(byte[] payload)
        {
            if (payload.Length > MaxFrameSize)
                throw new InvalidDataException("Frame too large: " + payload.Length);
            var framed = new byte[4 + payload.Length];
            WriteInt32LE(framed, 0, payload.Length);
            Buffer.BlockCopy(payload, 0, framed, 4, payload.Length);
            return framed;
        }

        public void Append(byte[] data, int offset, int count)
        {
            if (count <= 0) return;
            if (_end + count > _buf.Length)
            {
                int used = _end - _start;
                if (used + count > _buf.Length)
                {
                    var bigger = new byte[Math.Max(_buf.Length * 2, used + count)];
                    Buffer.BlockCopy(_buf, _start, bigger, 0, used);
                    _buf = bigger;
                }
                else
                {
                    Buffer.BlockCopy(_buf, _start, _buf, 0, used);
                }
                _start = 0;
                _end = used;
            }
            Buffer.BlockCopy(data, offset, _buf, _end, count);
            _end += count;
        }

        public bool TryReadFrame(out byte[] payload)
        {
            payload = null;
            int available = _end - _start;
            if (available < 4) return false;
            int len = ReadInt32LE(_buf, _start);
            if (len < 0 || len > MaxFrameSize)
                throw new InvalidDataException("Bad frame length: " + len);
            if (available < 4 + len) return false;
            payload = new byte[len];
            Buffer.BlockCopy(_buf, _start + 4, payload, 0, len);
            _start += 4 + len;
            if (_start == _end) { _start = 0; _end = 0; }
            return true;
        }

        private static void WriteInt32LE(byte[] b, int o, int v)
        {
            b[o] = (byte)v;
            b[o + 1] = (byte)(v >> 8);
            b[o + 2] = (byte)(v >> 16);
            b[o + 3] = (byte)(v >> 24);
        }

        private static int ReadInt32LE(byte[] b, int o)
        {
            return b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24);
        }
    }
}
