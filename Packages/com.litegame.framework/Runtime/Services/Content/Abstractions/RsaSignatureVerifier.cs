using System;

namespace LiteFramework
{
    /// <summary>
    /// RSA PKCS#1 v1.5 + SHA-256 验证器（唯一生产实现；测试可用 <see cref="ISignatureVerifier"/> 替身）。
    /// 公钥来自 <see cref="TrustedKeyRing"/>——**客户端只带公钥**。
    /// </summary>
    public sealed class RsaSignatureVerifier : ISignatureVerifier, IDisposable
    {
        private readonly System.Security.Cryptography.RSA _rsa;

        public string KeyId { get; }

        public RsaSignatureVerifier(string keyId, byte[] modulus, byte[] exponent)
        {
            if (string.IsNullOrEmpty(keyId)) throw new ArgumentException("keyId 不能为空", nameof(keyId));
            if (modulus == null || modulus.Length == 0) throw new ArgumentException("模数不能为空", nameof(modulus));
            if (exponent == null || exponent.Length == 0) throw new ArgumentException("指数不能为空", nameof(exponent));

            KeyId = keyId;
            _rsa = System.Security.Cryptography.RSA.Create();
            _rsa.ImportParameters(new System.Security.Cryptography.RSAParameters
            {
                Modulus = modulus,
                Exponent = exponent,
            });
        }

        /// <summary>从受信公钥集构造（keyId 未登记/已撤销 → null）。</summary>
        public static RsaSignatureVerifier FromKeyRing(TrustedKeyRing ring, string keyId)
        {
            if (ring == null || !ring.TryGet(keyId, out var entry)) return null;
            return new RsaSignatureVerifier(entry.KeyId, entry.Modulus, entry.Exponent);
        }

        /// <summary>验证（失败返回 false，不抛——候选校验常规路径）。</summary>
        public bool Verify(byte[] data, byte[] signature)
        {
            if (data == null || signature == null || signature.Length == 0) return false;
            try
            {
                return _rsa.VerifyData(data, signature,
                    System.Security.Cryptography.HashAlgorithmName.SHA256,
                    System.Security.Cryptography.RSASignaturePadding.Pkcs1);
            }
            catch (Exception)
            {
                return false;   // 畸形签名/参数异常一律按"验证不通过"（不把异常当通过）
            }
        }

        public void Dispose() => _rsa?.Dispose();
    }
}
