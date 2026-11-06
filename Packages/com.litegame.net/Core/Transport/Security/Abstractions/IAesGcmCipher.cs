using System;

namespace LiteNet.Transport
{
    /// <summary>
    /// AEAD 密码器端口（安全信封的唯一密码学依赖）：平台实现与托管回退实现共用同一形状， 让 <see cref="SecureEnvelopeSession"/> 对"运行时有没有 AES-GCM"无感。
    /// </summary>
    internal interface IAesGcmCipher : IDisposable
    {
        void Encrypt(byte[] nonce, byte[] plaintext, byte[] ciphertext, byte[] tag, byte[] associatedData);

        bool TryDecrypt(byte[] nonce, byte[] ciphertext, byte[] tag, byte[] plaintext, byte[] associatedData);
    }
}
