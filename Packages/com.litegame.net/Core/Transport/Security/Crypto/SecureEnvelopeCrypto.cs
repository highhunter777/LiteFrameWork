using System;
using System.Security.Cryptography;
using System.Text;

namespace LiteNet.Transport
{
    internal sealed class SecureEnvelopeKeys
    {
        internal readonly byte[] ClientKey;
        internal readonly byte[] ServerKey;
        internal readonly byte[] ClientNoncePrefix;
        internal readonly byte[] ServerNoncePrefix;

        internal SecureEnvelopeKeys(byte[] material)
        {
            ClientKey = Copy(material, 0, 32);
            ServerKey = Copy(material, 32, 32);
            ClientNoncePrefix = Copy(material, 64, 4);
            ServerNoncePrefix = Copy(material, 68, 4);
        }

        private static byte[] Copy(byte[] source, int offset, int count)
        {
            var result = new byte[count];
            Buffer.BlockCopy(source, offset, result, 0, count);
            return result;
        }
    }

    internal static class SecureEnvelopeCrypto
    {
        private static readonly byte[] ServerLabel = Encoding.UTF8.GetBytes("LiteNet/SecureEnvelope/v1/server");
        private static readonly byte[] ClientLabel = Encoding.UTF8.GetBytes("LiteNet/SecureEnvelope/v1/client");
        private static readonly byte[] ExpandLabel = Encoding.UTF8.GetBytes("LiteNet/SecureEnvelope/v1/session");

        internal static byte[] CreateServerProof(byte[] preSharedKey, byte[] clientHello, byte[] serverNonce)
        {
            return Hmac(preSharedKey, Concat(ServerLabel, clientHello, serverNonce));
        }

        internal static byte[] CreateClientProof(byte[] preSharedKey, byte[] clientHello, byte[] serverHelloTranscript)
        {
            return Hmac(preSharedKey, Concat(ClientLabel, clientHello, serverHelloTranscript));
        }

        internal static bool FixedEquals(byte[] left, byte[] right)
        {
            return left != null && right != null && left.Length == right.Length &&
                CryptographicOperations.FixedTimeEquals(left, right);
        }

        internal static SecureEnvelopeKeys DeriveKeys(byte[] preSharedKey, byte[] clientNonce, byte[] serverNonce)
        {
            byte[] salt = Sha256(Concat(clientNonce, serverNonce));
            byte[] prk = Hmac(salt, preSharedKey);
            return new SecureEnvelopeKeys(HkdfExpand(prk, ExpandLabel, 72));
        }

        internal static byte[] CreateNonce(byte[] prefix, long sequence)
        {
            var nonce = new byte[12];
            Buffer.BlockCopy(prefix, 0, nonce, 0, 4);
            ulong value = (ulong)sequence;
            for (int i = 0; i < 8; i++) nonce[4 + i] = (byte)(value >> (8 * i));
            return nonce;
        }

        private static byte[] HkdfExpand(byte[] prk, byte[] info, int length)
        {
            var output = new byte[length];
            var previous = Array.Empty<byte>();
            int written = 0;
            byte counter = 1;
            while (written < length)
            {
                previous = Hmac(prk, Concat(previous, info, new[] { counter++ }));
                int count = Math.Min(previous.Length, length - written);
                Buffer.BlockCopy(previous, 0, output, written, count);
                written += count;
            }
            return output;
        }

        private static byte[] Sha256(byte[] data)
        {
            using (var sha = SHA256.Create()) return sha.ComputeHash(data);
        }

        private static byte[] Hmac(byte[] key, byte[] data)
        {
            using (var hmac = new HMACSHA256(key)) return hmac.ComputeHash(data);
        }

        private static byte[] Concat(params byte[][] parts)
        {
            int length = 0;
            for (int i = 0; i < parts.Length; i++) length += parts[i]?.Length ?? 0;
            var result = new byte[length];
            int offset = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] == null || parts[i].Length == 0) continue;
                Buffer.BlockCopy(parts[i], 0, result, offset, parts[i].Length);
                offset += parts[i].Length;
            }
            return result;
        }
    }
}
