using System;

namespace LiteNet.Transport
{
    /// <summary>
    /// AEAD 密码器端口（安全信封的唯一密码学依赖）：平台实现与托管回退实现共用同一形状，
    /// 让 <see cref="SecureEnvelopeSession"/> 对"运行时有没有 AES-GCM"无感。
    ///
    /// 语义契约：<see cref="TryDecrypt"/> **不抛**认证失败——标签不符返回 false（调用方按不可信包丢弃）；
    /// 只有用法错误（长度不配、已释放）才抛。
    /// </summary>
    internal interface IAesGcmCipher : IDisposable
    {
        void Encrypt(byte[] nonce, byte[] plaintext, byte[] ciphertext, byte[] tag, byte[] associatedData);

        bool TryDecrypt(byte[] nonce, byte[] ciphertext, byte[] tag, byte[] plaintext, byte[] associatedData);
    }
}
