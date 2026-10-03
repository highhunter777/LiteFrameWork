using System;
using System.Security.Cryptography;

namespace LiteNet.Transport
{
    internal sealed class SecureEnvelopeSession : IDisposable
    {
        private const long ReplayWindowBits = 64;

        private readonly AesGcm _sendCipher;
        private readonly AesGcm _receiveCipher;
        private readonly byte[] _sendNoncePrefix;
        private readonly byte[] _receiveNoncePrefix;
        private readonly int _maxPayloadBytes;
        private readonly object _sync = new object();
        private long _nextSendSequence = 1;
        private long _highestReceivedSequence;
        private ulong _receivedBits;
        private bool _disposed;

        private SecureEnvelopeSession(byte[] sendKey, byte[] receiveKey, byte[] sendNoncePrefix,
            byte[] receiveNoncePrefix, int maxPayloadBytes)
        {
            _sendCipher = CreateCipher(sendKey);
            _receiveCipher = CreateCipher(receiveKey);
            _sendNoncePrefix = sendNoncePrefix;
            _receiveNoncePrefix = receiveNoncePrefix;
            _maxPayloadBytes = maxPayloadBytes;
        }

        internal static SecureEnvelopeSession CreateClient(SecureEnvelopeKeys keys, int maxPayloadBytes)
        {
            return new SecureEnvelopeSession(keys.ClientKey, keys.ServerKey,
                keys.ClientNoncePrefix, keys.ServerNoncePrefix, maxPayloadBytes);
        }

        internal static SecureEnvelopeSession CreateServer(SecureEnvelopeKeys keys, int maxPayloadBytes)
        {
            return new SecureEnvelopeSession(keys.ServerKey, keys.ClientKey,
                keys.ServerNoncePrefix, keys.ClientNoncePrefix, maxPayloadBytes);
        }

        internal byte[] Encrypt(ArraySegment<byte> plaintext, bool reliable)
        {
            if (plaintext.Array == null) throw new ArgumentException("Payload array is required.", nameof(plaintext));
            if (plaintext.Count > _maxPayloadBytes) throw new ArgumentOutOfRangeException(nameof(plaintext));

            lock (_sync)
            {
                ThrowIfDisposed();
                if (_nextSendSequence == long.MaxValue) throw new InvalidOperationException("Secure envelope send sequence exhausted.");
                long sequence = _nextSendSequence++;
                byte[] header = SecureEnvelopeProtocol.CreateDataHeader(reliable, sequence, plaintext.Count);
                byte[] source = Copy(plaintext);
                var ciphertext = new byte[source.Length];
                var tag = new byte[SecureEnvelopeProtocol.TagBytes];
                _sendCipher.Encrypt(SecureEnvelopeCrypto.CreateNonce(_sendNoncePrefix, sequence), source,
                    ciphertext, tag, header);

                var packet = new byte[header.Length + ciphertext.Length + tag.Length];
                Buffer.BlockCopy(header, 0, packet, 0, header.Length);
                Buffer.BlockCopy(ciphertext, 0, packet, header.Length, ciphertext.Length);
                Buffer.BlockCopy(tag, 0, packet, header.Length + ciphertext.Length, tag.Length);
                return packet;
            }
        }

        internal bool TryDecrypt(ArraySegment<byte> packet, out bool reliable, out byte[] plaintext)
        {
            reliable = false;
            plaintext = null;
            lock (_sync)
            {
                ThrowIfDisposed();
                if (!SecureEnvelopeProtocol.TryParseData(packet, _maxPayloadBytes, out reliable,
                    out long sequence, out byte[] header, out byte[] ciphertext, out byte[] tag)) return false;
                if (!CanAccept(sequence)) return false;

                try
                {
                    plaintext = new byte[ciphertext.Length];
                    _receiveCipher.Decrypt(SecureEnvelopeCrypto.CreateNonce(_receiveNoncePrefix, sequence),
                        ciphertext, tag, plaintext, header);
                }
                catch (CryptographicException)
                {
                    plaintext = null;
                    return false;
                }

                MarkAccepted(sequence);
                return true;
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                _sendCipher.Dispose();
                _receiveCipher.Dispose();
                Array.Clear(_sendNoncePrefix, 0, _sendNoncePrefix.Length);
                Array.Clear(_receiveNoncePrefix, 0, _receiveNoncePrefix.Length);
            }
        }

        private bool CanAccept(long sequence)
        {
            if (sequence <= 0) return false;
            if (_highestReceivedSequence == 0 || sequence > _highestReceivedSequence) return true;
            long delta = _highestReceivedSequence - sequence;
            return delta < ReplayWindowBits && (_receivedBits & (1UL << (int)delta)) == 0;
        }

        private void MarkAccepted(long sequence)
        {
            if (_highestReceivedSequence == 0)
            {
                _highestReceivedSequence = sequence;
                _receivedBits = 1;
                return;
            }

            if (sequence > _highestReceivedSequence)
            {
                long delta = sequence - _highestReceivedSequence;
                _receivedBits = delta >= ReplayWindowBits ? 1UL : (_receivedBits << (int)delta) | 1UL;
                _highestReceivedSequence = sequence;
                return;
            }

            _receivedBits |= 1UL << (int)(_highestReceivedSequence - sequence);
        }

        private static byte[] Copy(ArraySegment<byte> value)
        {
            var result = new byte[value.Count];
            Buffer.BlockCopy(value.Array, value.Offset, result, 0, value.Count);
            return result;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SecureEnvelopeSession));
        }

        private static AesGcm CreateCipher(byte[] key)
        {
#if NET8_0_OR_GREATER
            return new AesGcm(key, SecureEnvelopeProtocol.TagBytes);
#else
            return new AesGcm(key);
#endif
        }
    }
}
