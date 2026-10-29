using System;

namespace LiteNet.Transport
{
    internal static class SecureEnvelopeProtocol
    {
        internal const byte Version = 1;
        internal const byte ClientHello = 1;
        internal const byte ServerHello = 2;
        internal const byte ClientFinish = 3;
        internal const byte Data = 4;
        internal const int NonceBytes = 32;
        internal const int ProofBytes = 32;
        internal const int TagBytes = 16;
        internal const int DataHeaderBytes = 19;

        private const byte Magic0 = (byte)'L';
        private const byte Magic1 = (byte)'S';
        private const byte Magic2 = (byte)'E';
        private const byte Magic3 = (byte)'1';
        private const int HandshakeHeaderBytes = 6;

        internal static byte[] CreateClientHello(byte[] clientNonce)
        {
            ValidateNonce(clientNonce, nameof(clientNonce));
            var packet = CreateHandshakeHeader(ClientHello, NonceBytes);
            Buffer.BlockCopy(clientNonce, 0, packet, HandshakeHeaderBytes, NonceBytes);
            return packet;
        }

        internal static byte[] CreateServerHello(byte[] clientNonce, byte[] serverNonce, byte[] proof)
        {
            ValidateNonce(clientNonce, nameof(clientNonce));
            ValidateNonce(serverNonce, nameof(serverNonce));
            if (proof == null || proof.Length != ProofBytes)
                throw new ArgumentException("Invalid server proof length.", nameof(proof));

            var packet = CreateHandshakeHeader(ServerHello, NonceBytes + NonceBytes + ProofBytes);
            Buffer.BlockCopy(clientNonce, 0, packet, HandshakeHeaderBytes, NonceBytes);
            Buffer.BlockCopy(serverNonce, 0, packet, HandshakeHeaderBytes + NonceBytes, NonceBytes);
            Buffer.BlockCopy(proof, 0, packet, HandshakeHeaderBytes + NonceBytes + NonceBytes, ProofBytes);
            return packet;
        }

        internal static byte[] CreateServerHelloTranscript(byte[] clientNonce, byte[] serverNonce)
        {
            ValidateNonce(clientNonce, nameof(clientNonce));
            ValidateNonce(serverNonce, nameof(serverNonce));
            var packet = CreateHandshakeHeader(ServerHello, NonceBytes + NonceBytes);
            Buffer.BlockCopy(clientNonce, 0, packet, HandshakeHeaderBytes, NonceBytes);
            Buffer.BlockCopy(serverNonce, 0, packet, HandshakeHeaderBytes + NonceBytes, NonceBytes);
            return packet;
        }

        internal static byte[] CreateClientFinish(byte[] proof)
        {
            if (proof == null || proof.Length != ProofBytes)
                throw new ArgumentException("Invalid client proof length.", nameof(proof));
            var packet = CreateHandshakeHeader(ClientFinish, ProofBytes);
            Buffer.BlockCopy(proof, 0, packet, HandshakeHeaderBytes, ProofBytes);
            return packet;
        }

        internal static byte[] CreateDataHeader(bool reliable, long sequence, int ciphertextBytes)
        {
            if (sequence <= 0) throw new ArgumentOutOfRangeException(nameof(sequence));
            if (ciphertextBytes < 0) throw new ArgumentOutOfRangeException(nameof(ciphertextBytes));
            var header = new byte[DataHeaderBytes];
            WritePrefix(header, Data);
            header[6] = reliable ? (byte)1 : (byte)0;
            WriteInt64(header, 7, sequence);
            WriteInt32(header, 15, ciphertextBytes);
            return header;
        }

        internal static bool TryParseClientHello(ArraySegment<byte> packet, out byte[] clientNonce)
        {
            clientNonce = null;
            if (!HasHandshakeHeader(packet, ClientHello, NonceBytes)) return false;
            clientNonce = Slice(packet, HandshakeHeaderBytes, NonceBytes);
            return true;
        }

        internal static bool TryParseServerHello(ArraySegment<byte> packet, out byte[] clientNonce,
            out byte[] serverNonce, out byte[] proof)
        {
            clientNonce = null;
            serverNonce = null;
            proof = null;
            if (!HasHandshakeHeader(packet, ServerHello, NonceBytes + NonceBytes + ProofBytes)) return false;
            clientNonce = Slice(packet, HandshakeHeaderBytes, NonceBytes);
            serverNonce = Slice(packet, HandshakeHeaderBytes + NonceBytes, NonceBytes);
            proof = Slice(packet, HandshakeHeaderBytes + NonceBytes + NonceBytes, ProofBytes);
            return true;
        }

        internal static bool TryParseClientFinish(ArraySegment<byte> packet, out byte[] proof)
        {
            proof = null;
            if (!HasHandshakeHeader(packet, ClientFinish, ProofBytes)) return false;
            proof = Slice(packet, HandshakeHeaderBytes, ProofBytes);
            return true;
        }

        internal static bool TryParseData(ArraySegment<byte> packet, int maxPayloadBytes,
            out bool reliable, out long sequence, out byte[] header, out byte[] ciphertext, out byte[] tag)
        {
            reliable = false;
            sequence = 0;
            header = null;
            ciphertext = null;
            tag = null;
            if (packet.Array == null || packet.Count < DataHeaderBytes + TagBytes) return false;
            if (!HasPrefix(packet, Data)) return false;

            int channel = packet.Array[packet.Offset + 6];
            if (channel > 1) return false;
            reliable = channel == 1;
            sequence = ReadInt64(packet.Array, packet.Offset + 7);
            int ciphertextBytes = ReadInt32(packet.Array, packet.Offset + 15);
            if (sequence <= 0 || ciphertextBytes < 0 || ciphertextBytes > maxPayloadBytes) return false;
            if (packet.Count != DataHeaderBytes + ciphertextBytes + TagBytes) return false;

            header = Slice(packet, 0, DataHeaderBytes);
            ciphertext = Slice(packet, DataHeaderBytes, ciphertextBytes);
            tag = Slice(packet, DataHeaderBytes + ciphertextBytes, TagBytes);
            return true;
        }

        private static byte[] CreateHandshakeHeader(byte kind, int payloadBytes)
        {
            var packet = new byte[HandshakeHeaderBytes + payloadBytes];
            WritePrefix(packet, kind);
            return packet;
        }

        private static bool HasHandshakeHeader(ArraySegment<byte> packet, byte kind, int payloadBytes)
        {
            return packet.Count == HandshakeHeaderBytes + payloadBytes && HasPrefix(packet, kind);
        }

        private static bool HasPrefix(ArraySegment<byte> packet, byte kind)
        {
            if (packet.Array == null || packet.Count < HandshakeHeaderBytes) return false;
            int offset = packet.Offset;
            return packet.Array[offset] == Magic0 && packet.Array[offset + 1] == Magic1 &&
                   packet.Array[offset + 2] == Magic2 && packet.Array[offset + 3] == Magic3 &&
                   packet.Array[offset + 4] == Version && packet.Array[offset + 5] == kind;
        }

        private static void WritePrefix(byte[] target, byte kind)
        {
            target[0] = Magic0;
            target[1] = Magic1;
            target[2] = Magic2;
            target[3] = Magic3;
            target[4] = Version;
            target[5] = kind;
        }

        private static byte[] Slice(ArraySegment<byte> source, int offset, int count)
        {
            var result = new byte[count];
            Buffer.BlockCopy(source.Array, source.Offset + offset, result, 0, count);
            return result;
        }

        private static void ValidateNonce(byte[] nonce, string name)
        {
            if (nonce == null || nonce.Length != NonceBytes)
                throw new ArgumentException("Invalid nonce length.", name);
        }

        private static void WriteInt32(byte[] target, int offset, int value)
        {
            target[offset] = (byte)value;
            target[offset + 1] = (byte)(value >> 8);
            target[offset + 2] = (byte)(value >> 16);
            target[offset + 3] = (byte)(value >> 24);
        }

        private static void WriteInt64(byte[] target, int offset, long value)
        {
            ulong bits = (ulong)value;
            for (int i = 0; i < 8; i++) target[offset + i] = (byte)(bits >> (8 * i));
        }

        private static int ReadInt32(byte[] source, int offset)
        {
            return source[offset] | source[offset + 1] << 8 | source[offset + 2] << 16 | source[offset + 3] << 24;
        }

        private static long ReadInt64(byte[] source, int offset)
        {
            ulong value = 0;
            for (int i = 0; i < 8; i++) value |= (ulong)source[offset + i] << (8 * i);
            return (long)value;
        }
    }
}
