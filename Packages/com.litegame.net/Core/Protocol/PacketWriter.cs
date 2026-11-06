using System;
using System.IO;
using Google.Protobuf;

namespace LiteNet.Protocol
{
    /// <summary>
    /// 复用信封编码器（输入包 60Hz，`PacketCodec.Encode` 每包两次分配 → GC 压力）。
    /// </summary>
    public sealed class PacketWriter : IDisposable
    {
        private byte[] _buffer;
        private MemoryStream _stream;
        private CodedOutputStream _out;
        private bool _disposed;

        public PacketWriter(int initialCapacity = 512)
        {
            _buffer = new byte[Math.Max(16, initialCapacity)];
            Rebind();
        }

        /// <summary>当前缓冲容量（诊断/测试用）。</summary>
        public int Capacity => _buffer.Length;

        /// <summary>
        /// 编码一包并返回可发送段（**下次 Write 前有效**）。
        /// 载荷按 `CalculateSize` 精确预留 → 缓冲按需增长（不截断、不静默丢包）。
        /// </summary>
        public ArraySegment<byte> Write(PacketType type, IMessage message)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PacketWriter));
            if (message == null) throw new ArgumentNullException(nameof(message));

            int length = 1 + message.CalculateSize();   // CalculateSize 精确 → 无需读回流位置
            if (_buffer.Length < length) Grow(length);

            _buffer[0] = (byte)type;                    // 信封：[1B PacketType][payload]
            _stream.Position = 1;
            message.WriteTo(_out);
            _out.Flush();

            return new ArraySegment<byte>(_buffer, 0, length);
        }

        private void Grow(int length)
        {
            int next = _buffer.Length;
            while (next < length) next *= 2;

            _out?.Dispose();                            // 旧流绑在旧缓冲上 → 全部重建
            _stream?.Dispose();
            _buffer = new byte[next];
            Rebind();
        }

        private void Rebind()
        {
            _stream = new MemoryStream(_buffer, writable: true);
            _out = new CodedOutputStream(_stream, leaveOpen: true);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _out?.Dispose();
            _stream?.Dispose();
            _out = null;
            _stream = null;
        }
    }
}
