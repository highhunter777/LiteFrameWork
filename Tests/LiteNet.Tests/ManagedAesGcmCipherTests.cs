using System;
using System.Security.Cryptography;
using LiteNet.Transport;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 托管 AES-GCM 回退（<see cref="ManagedAesGcmCipher"/>）与平台 <see cref="AesGcm"/> 的**逐位互通**对账。
    ///
    /// 为什么是硬契约：Unity Mono 运行时没有平台 AEAD 实现（安全信封会回退到托管实现），而服务端跑在
    /// .NET 8 的平台 <c>AesGcm</c> 上——两端必须在同密钥/同 nonce/同 AAD 下产出相同的密文与标签，
    /// 否则握手后第一个数据包就解不开。本组用例即该契约的守卫。
    /// </summary>
    public sealed class ManagedAesGcmCipherTests
    {
        private const int TagBytes = 16;
        private const int KeyBytes = 32;
        private const int NonceBytes = 12;

        [Fact]
        public void 托管与平台_同密钥同nonce同AAD_密文与标签逐位相等()
        {
            foreach (int size in new[] { 0, 1, 15, 16, 17, 31, 64, 1023, 4096, 65536 })
            foreach (int aadSize in new[] { 0, 1, 19, 32 })
            {
                byte[] key = Fill(0x40, KeyBytes);
                byte[] nonce = Fill(0x10, NonceBytes);
                byte[] plaintext = Fill((byte)(size & 0xFF), size);
                byte[] aad = Fill((byte)(aadSize & 0xFF), aadSize);

                var managedCiphertext = new byte[size];
                var managedTag = new byte[TagBytes];
                using (var managed = new ManagedAesGcmCipher(key, TagBytes))
                    managed.Encrypt(nonce, plaintext, managedCiphertext, managedTag, aad);

                var platformCiphertext = new byte[size];
                var platformTag = new byte[TagBytes];
                using (var platform = new AesGcm(key, TagBytes))
                    platform.Encrypt(nonce, plaintext, platformCiphertext, platformTag, aad);

                Assert.Equal(platformCiphertext, managedCiphertext);
                Assert.Equal(platformTag, managedTag);
            }
        }

        [Fact]
        public void 双向互解_托管解平台密文_平台解托管密文()
        {
            byte[] key = Fill(0x51, KeyBytes);
            byte[] nonce = Fill(0x22, NonceBytes);
            byte[] plaintext = Fill(0x33, 257);
            byte[] aad = Fill(0x44, 19);

            var managedCiphertext = new byte[plaintext.Length];
            var managedTag = new byte[TagBytes];
            using (var managed = new ManagedAesGcmCipher(key, TagBytes))
                managed.Encrypt(nonce, plaintext, managedCiphertext, managedTag, aad);

            var platformRecovered = new byte[plaintext.Length];
            using (var platform = new AesGcm(key, TagBytes))
                platform.Decrypt(nonce, managedCiphertext, managedTag, platformRecovered, aad);
            Assert.Equal(plaintext, platformRecovered);

            var platformCiphertext = new byte[plaintext.Length];
            var platformTag = new byte[TagBytes];
            using (var platform = new AesGcm(key, TagBytes))
                platform.Encrypt(nonce, plaintext, platformCiphertext, platformTag, aad);

            var managedRecovered = new byte[plaintext.Length];
            bool ok;
            using (var managed = new ManagedAesGcmCipher(key, TagBytes))
                ok = managed.TryDecrypt(nonce, platformCiphertext, platformTag, managedRecovered, aad);
            Assert.True(ok);
            Assert.Equal(plaintext, managedRecovered);
        }

        [Fact]
        public void 篡改_密文或标签或AAD_一律拒收()
        {
            byte[] key = Fill(0x61, KeyBytes);
            byte[] nonce = Fill(0x72, NonceBytes);
            byte[] plaintext = Fill(0x83, 64);
            byte[] aad = Fill(0x94, 19);

            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[TagBytes];
            using (var managed = new ManagedAesGcmCipher(key, TagBytes))
                managed.Encrypt(nonce, plaintext, ciphertext, tag, aad);

            var recovered = new byte[plaintext.Length];
            using (var managed = new ManagedAesGcmCipher(key, TagBytes))
            {
                var flippedCiphertext = (byte[])ciphertext.Clone();
                flippedCiphertext[0] ^= 0x01;
                Assert.False(managed.TryDecrypt(nonce, flippedCiphertext, tag, recovered, aad));

                var flippedTag = (byte[])tag.Clone();
                flippedTag[TagBytes - 1] ^= 0x01;
                Assert.False(managed.TryDecrypt(nonce, ciphertext, flippedTag, recovered, aad));

                var flippedAad = (byte[])aad.Clone();
                flippedAad[3] ^= 0x01;
                Assert.False(managed.TryDecrypt(nonce, ciphertext, tag, recovered, flippedAad));

                Assert.True(managed.TryDecrypt(nonce, ciphertext, tag, recovered, aad));
                Assert.Equal(plaintext, recovered);
            }
        }

        [Fact]
        public void 错密钥_解不开()
        {
            byte[] nonce = Fill(0xA5, NonceBytes);
            byte[] plaintext = Fill(0xB6, 32);
            byte[] ciphertext = new byte[plaintext.Length];
            byte[] tag = new byte[TagBytes];
            using (var sender = new ManagedAesGcmCipher(Fill(0xC7, KeyBytes), TagBytes))
                sender.Encrypt(nonce, plaintext, ciphertext, tag, null);

            var recovered = new byte[plaintext.Length];
            using (var receiver = new ManagedAesGcmCipher(Fill(0xD8, KeyBytes), TagBytes))
                Assert.False(receiver.TryDecrypt(nonce, ciphertext, tag, recovered, null));
        }

        [Fact]
        public void 空明文与空AAD_往返成立且与平台互为等价()
        {
            byte[] key = Fill(0xE9, KeyBytes);
            byte[] nonce = Fill(0xFA, NonceBytes);

            var managedTag = new byte[TagBytes];
            using (var managed = new ManagedAesGcmCipher(key, TagBytes))
                managed.Encrypt(nonce, new byte[0], new byte[0], managedTag, new byte[0]);

            var platformTag = new byte[TagBytes];
            using (var platform = new AesGcm(key, TagBytes))
                platform.Encrypt(nonce, new byte[0], new byte[0], platformTag, new byte[0]);

            Assert.Equal(platformTag, managedTag);

            using (var managed = new ManagedAesGcmCipher(key, TagBytes))
                Assert.True(managed.TryDecrypt(nonce, new byte[0], managedTag, new byte[0], new byte[0]));
        }

        [Fact]
        public void nonce非12字节_用法错误即抛()
        {
            using var cipher = new ManagedAesGcmCipher(Fill(0x0B, KeyBytes), TagBytes);
            Assert.Throws<ArgumentException>(() =>
                cipher.Encrypt(new byte[16], new byte[4], new byte[4], new byte[TagBytes], null));
        }

        /// <summary>确定性字节填充（不用 System.Random——用例输入不引入运行时随机源）。</summary>
        private static byte[] Fill(byte seed, int length)
        {
            var bytes = new byte[length];
            byte state = seed;
            for (int i = 0; i < length; i++)
            {
                state = (byte)(state * 31 + 17);
                bytes[i] = state;
            }
            return bytes;
        }
    }
}
