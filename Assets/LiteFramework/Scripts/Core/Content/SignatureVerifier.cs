using System;

namespace LiteFramework
{
    /// <summary>
    /// 内容签名验证端口（《热更与内容发布专项设计》§6"采用成熟签名实现和确定的签名字节编码；
    /// 客户端只带信任公钥，私钥由受控发布签名环境持有"）。
    ///
    /// **算法选型（2026-09-25 实测裁决）**：RSA-2048 + PKCS#1 v1.5 + SHA-256。
    /// 本工程实测 `ECDsa` / `ECDsaCng` 在 Mono 下抛 `NotImplementedException`（只有 Windows CNG 实现，
    /// 无 OpenSSL 回退；换 API 档位无效——问题在 BCL 裁剪不在档位），Ed25519 连类型都不存在。
    /// 签名每局只验一次清单、不在热路径，RSA 的签名长度代价可忽略。
    ///
    /// **验证语义**：
    /// - 对**原始字节**验证（不做任何换行/空白归一化——§5"二进制文件不做 CRLF 替换"）；
    /// - 失败一律返回 false，**不抛**（候选校验的常规路径，调用方据此拒绝候选）；
    /// - 公钥用「模数 + 指数」表示而非 SPKI：实测本工程不支持 `ImportSubjectPublicKeyInfo`，
    ///   而 `ImportParameters(Modulus, Exponent)` 跨运行时可用。
    /// </summary>
    public interface ISignatureVerifier
    {
        /// <summary>验证签名。data/signature 为 null 或公钥不可用 → false。</summary>
        bool Verify(byte[] data, byte[] signature);
    }

    /// <summary>
    /// 受信公钥集合（§6"密钥轮换/撤销"的最小落点）：按 keyId 查找公钥；**已撤销的 keyId 一律拒绝**
    /// （撤销优先于存在——轮换期旧公钥仍在集合里但被撤销时不得再通过）。
    /// </summary>
    public sealed class TrustedKeyRing
    {
        /// <summary>一条受信公钥（模数+指数 + 可选撤销标记）。</summary>
        public sealed class Entry
        {
            public string KeyId;
            public byte[] Modulus;
            public byte[] Exponent;

            /// <summary>已撤销（轮换/泄露处置）：撤销后该 keyId 的签名一律拒绝，即使公钥仍在。</summary>
            public bool Revoked;
        }

        private readonly System.Collections.Generic.Dictionary<string, Entry> _keys =
            new System.Collections.Generic.Dictionary<string, Entry>(StringComparer.Ordinal);

        public int Count => _keys.Count;

        /// <summary>登记受信公钥（同 keyId 覆盖）。空 keyId/模数/指数显性拒绝。</summary>
        public TrustedKeyRing Add(string keyId, byte[] modulus, byte[] exponent, bool revoked = false)
        {
            if (string.IsNullOrEmpty(keyId)) throw new ArgumentException("keyId 不能为空", nameof(keyId));
            if (modulus == null || modulus.Length == 0) throw new ArgumentException("模数不能为空", nameof(modulus));
            if (exponent == null || exponent.Length == 0) throw new ArgumentException("指数不能为空", nameof(exponent));

            _keys[keyId] = new Entry { KeyId = keyId, Modulus = modulus, Exponent = exponent, Revoked = revoked };
            return this;
        }

        /// <summary>撤销一个 keyId（轮换/泄露处置）。返回 false = 该 keyId 未登记。</summary>
        public bool Revoke(string keyId)
        {
            if (string.IsNullOrEmpty(keyId) || !_keys.TryGetValue(keyId, out var e)) return false;
            e.Revoked = true;
            return true;
        }

        /// <summary>取可用公钥；**未登记或已撤销都返回 false**（撤销优先于存在）。</summary>
        public bool TryGet(string keyId, out Entry entry)
        {
            entry = null;
            if (string.IsNullOrEmpty(keyId)) return false;
            if (!_keys.TryGetValue(keyId, out var e) || e.Revoked) return false;
            entry = e;
            return true;
        }
    }

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
