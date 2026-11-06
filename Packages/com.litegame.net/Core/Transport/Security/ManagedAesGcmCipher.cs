using System;
using System.Security.Cryptography;

namespace LiteNet.Transport
{
    /// <summary>
    /// 托管 AES-GCM（NIST SP 800-38D）——安全信封在**没有平台 AEAD 的运行时**上的等价实现。
    /// </summary>
    internal sealed class ManagedAesGcmCipher : IAesGcmCipher
    {
        private const int BlockBytes = 16;
        private const int NonceBytes = 12;
        private const ulong Reduction = 0xE100000000000000UL;

        private readonly Aes _aes;
        private readonly ICryptoTransform _blockCipher;
        private readonly byte[] _hashKey = new byte[BlockBytes];     // H = E(K, 0^128)
        private readonly byte[] _j0 = new byte[BlockBytes];          // J0 = nonce ‖ 0^31 ‖ 1
        private readonly byte[] _counter = new byte[BlockBytes];     // 计数块（J0 的 inc32 起）
        private readonly byte[] _keystream = new byte[BlockBytes];
        private readonly byte[] _ghashX = new byte[BlockBytes];      // GHASH 累加器
        private readonly byte[] _ghashPad = new byte[BlockBytes];    // 尾块/长度块的暂存
        private readonly byte[] _tagScratch = new byte[BlockBytes];
        private readonly int _tagBytes;
        private bool _disposed;

        internal ManagedAesGcmCipher(byte[] key, int tagBytes)
        {
            if (key == null || key.Length == 0) throw new ArgumentException("Key is required.", nameof(key));
            if (tagBytes < 12 || tagBytes > BlockBytes) throw new ArgumentOutOfRangeException(nameof(tagBytes));

            _tagBytes = tagBytes;
            _aes = Aes.Create();
            _aes.Mode = CipherMode.ECB;
            _aes.Padding = PaddingMode.None;
            _aes.Key = key;
            _blockCipher = _aes.CreateEncryptor();
            EncryptBlock(_ghashPad, 0, _hashKey, 0);                 // 全零输入 → H（_ghashPad 创建即为 0）
        }

        public void Encrypt(byte[] nonce, byte[] plaintext, byte[] ciphertext, byte[] tag, byte[] associatedData)
        {
            ThrowIfDisposed();
            if (plaintext == null || ciphertext == null) throw new ArgumentNullException(nameof(ciphertext));
            if (plaintext.Length != ciphertext.Length) throw new ArgumentException("Ciphertext length must match plaintext.");
            if (tag == null || tag.Length < _tagBytes) throw new ArgumentException("Tag buffer too small.", nameof(tag));

            PrepareJ0(nonce);
            Buffer.BlockCopy(_j0, 0, _counter, 0, BlockBytes);
            Increment32(_counter);
            Gctr(_counter, plaintext, ciphertext, plaintext.Length);  // C = GCTR(inc32(J0), P)

            Ghash(associatedData, ciphertext, ciphertext.Length);      // S = GHASH(A ‖ C ‖ len)
            EncryptBlock(_j0, 0, _tagScratch, 0);                      // E(K, J0)
            for (int i = 0; i < _tagBytes; i++) tag[i] = (byte)(_tagScratch[i] ^ _ghashX[i]);
        }

        public bool TryDecrypt(byte[] nonce, byte[] ciphertext, byte[] tag, byte[] plaintext, byte[] associatedData)
        {
            ThrowIfDisposed();
            if (ciphertext == null || plaintext == null) throw new ArgumentNullException(nameof(plaintext));
            if (ciphertext.Length != plaintext.Length) throw new ArgumentException("Plaintext length must match ciphertext.");
            if (tag == null || tag.Length < _tagBytes) return false;

            PrepareJ0(nonce);
            Ghash(associatedData, ciphertext, ciphertext.Length);
            EncryptBlock(_j0, 0, _tagScratch, 0);
            if (!FixedEquals(_tagScratch, _ghashX, tag)) return false;  // 先认证后解密（失败不留明文）

            Buffer.BlockCopy(_j0, 0, _counter, 0, BlockBytes);
            Increment32(_counter);
            Gctr(_counter, ciphertext, plaintext, ciphertext.Length);
            return true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _blockCipher.Dispose();
            _aes.Dispose();
            Array.Clear(_hashKey, 0, _hashKey.Length);
            Array.Clear(_j0, 0, _j0.Length);
            Array.Clear(_counter, 0, _counter.Length);
            Array.Clear(_keystream, 0, _keystream.Length);
            Array.Clear(_ghashX, 0, _ghashX.Length);
            Array.Clear(_tagScratch, 0, _tagScratch.Length);
        }

        /// <summary>J0 = nonce ‖ 0^31 ‖ 1（12 字节 nonce 的 GCM 缺省构造）。</summary>
        private void PrepareJ0(byte[] nonce)
        {
            if (nonce == null || nonce.Length != NonceBytes)
                throw new ArgumentException("Nonce must be 12 bytes.", nameof(nonce));
            Buffer.BlockCopy(nonce, 0, _j0, 0, NonceBytes);
            _j0[NonceBytes] = 0;
            _j0[NonceBytes + 1] = 0;
            _j0[NonceBytes + 2] = 0;
            _j0[NonceBytes + 3] = 1;
        }

        /// <summary>GCTR：以计数块起逐块异或密钥流。</summary>
        private void Gctr(byte[] counter, byte[] input, byte[] output, int length)
        {
            int offset = 0;
            while (offset < length)
            {
                EncryptBlock(counter, 0, _keystream, 0);
                int block = Math.Min(BlockBytes, length - offset);
                for (int i = 0; i < block; i++) output[offset + i] = (byte)(input[offset + i] ^ _keystream[i]);
                Increment32(counter);
                offset += block;
            }
        }

        /// <summary>GHASH_H(A ‖ 0^v ‖ C ‖ 0^u ‖ [len(A)]_64 ‖ [len(C)]_64)：结果落在 <see cref="_ghashX"/>。</summary>
        private void Ghash(byte[] associatedData, byte[] ciphertext, int ciphertextLength)
        {
            Array.Clear(_ghashX, 0, BlockBytes);

            int aadLength = associatedData?.Length ?? 0;
            int offset = 0;
            while (offset + BlockBytes <= aadLength)
            {
                XorBlock(_ghashX, associatedData, offset);
                MultiplyHash();
                offset += BlockBytes;
            }
            if (offset < aadLength)
            {
                Array.Clear(_ghashPad, 0, BlockBytes);
                Buffer.BlockCopy(associatedData, offset, _ghashPad, 0, aadLength - offset);
                XorBlock(_ghashX, _ghashPad, 0);
                MultiplyHash();
            }

            offset = 0;
            while (offset + BlockBytes <= ciphertextLength)
            {
                XorBlock(_ghashX, ciphertext, offset);
                MultiplyHash();
                offset += BlockBytes;
            }
            if (offset < ciphertextLength)
            {
                Array.Clear(_ghashPad, 0, BlockBytes);
                Buffer.BlockCopy(ciphertext, offset, _ghashPad, 0, ciphertextLength - offset);
                XorBlock(_ghashX, _ghashPad, 0);
                MultiplyHash();
            }

            Array.Clear(_ghashPad, 0, BlockBytes);
            WriteUInt64Be(_ghashPad, 0, (ulong)aadLength * 8UL);
            WriteUInt64Be(_ghashPad, 8, (ulong)ciphertextLength * 8UL);
            XorBlock(_ghashX, _ghashPad, 0);
            MultiplyHash();
        }

        /// <summary>X ← X·H（GCM 反射约定：位序 MSB→LSB、右移约简 <c>0xE1</c>）。</summary>
        private void MultiplyHash()
        {
            ulong x0 = ReadUInt64Be(_ghashX, 0);
            ulong x1 = ReadUInt64Be(_ghashX, 8);
            ulong v0 = ReadUInt64Be(_hashKey, 0);
            ulong v1 = ReadUInt64Be(_hashKey, 8);
            ulong z0 = 0UL;
            ulong z1 = 0UL;

            for (int word = 0; word < 2; word++)
            {
                ulong bits = word == 0 ? x0 : x1;
                for (int i = 0; i < 64; i++)
                {
                    if (((bits >> (63 - i)) & 1UL) != 0UL)
                    {
                        z0 ^= v0;
                        z1 ^= v1;
                    }

                    bool shiftedOut = (v1 & 1UL) != 0UL;
                    v1 = (v1 >> 1) | (v0 << 63);
                    v0 >>= 1;
                    if (shiftedOut) v0 ^= Reduction;
                }
            }

            WriteUInt64Be(_ghashX, 0, z0);
            WriteUInt64Be(_ghashX, 8, z1);
        }

        private void EncryptBlock(byte[] input, int inputOffset, byte[] output, int outputOffset)
            => _blockCipher.TransformBlock(input, inputOffset, BlockBytes, output, outputOffset);

        private static void XorBlock(byte[] target, byte[] source, int sourceOffset)
        {
            for (int i = 0; i < BlockBytes; i++) target[i] ^= source[sourceOffset + i];
        }

        /// <summary>块计数加一（仅最低 32 位，大端；wraps mod 2^32——SP 800-38D inc32）。</summary>
        private static void Increment32(byte[] counter)
        {
            for (int i = BlockBytes - 1; i >= 12; i--)
            {
                if (++counter[i] != 0) return;
            }
        }

        private bool FixedEquals(byte[] tagBase, byte[] ghash, byte[] candidate)
        {
            byte accumulated = 0;
            for (int i = 0; i < _tagBytes; i++) accumulated |= (byte)(tagBase[i] ^ ghash[i] ^ candidate[i]);
            return accumulated == 0;
        }

        private static ulong ReadUInt64Be(byte[] source, int offset)
        {
            ulong value = 0UL;
            for (int i = 0; i < 8; i++) value = (value << 8) | source[offset + i];
            return value;
        }

        private static void WriteUInt64Be(byte[] target, int offset, ulong value)
        {
            for (int i = 0; i < 8; i++) target[offset + i] = (byte)(value >> (56 - 8 * i));
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ManagedAesGcmCipher));
        }
    }
}
