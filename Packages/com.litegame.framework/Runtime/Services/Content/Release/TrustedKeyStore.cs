using System;

namespace LiteFramework
{
    /// <summary>
    /// 受信公钥库（《热更与内容发布专项设计》§6"客户端只带信任公钥，私钥由受控发布签名环境持有"、 "必须定义根信任、密钥轮换/撤销"）。
    /// </summary>
    public sealed class TrustedKeyStore
    {
        private readonly TrustedKeyRing _ring = new TrustedKeyRing();

        /// <summary>已登记公钥数（含已撤销）。</summary>
        public int ProbeCount => _ring.Count;

        /// <summary>登记一把受信公钥（装配点调用；轮换期新旧并存）。</summary>
        public TrustedKeyStore Add(string keyId, byte[] modulus, byte[] exponent, bool revoked = false)
        {
            _ring.Add(keyId, modulus, exponent, revoked);
            return this;
        }

        /// <summary>撤销一个 keyId（泄露处置/轮换收尾）。撤销后该 keyId 的签名一律拒绝。</summary>
        public bool Revoke(string keyId) => _ring.Revoke(keyId);

        /// <summary>
        /// 按清单声明的 keyId 解析验签器。
        /// </summary>
        public ISignatureVerifier Resolve(string keyId)
            => RsaSignatureVerifier.FromKeyRing(_ring, keyId);

        /// <summary>供 <c>PatchRunner</c> 直接使用的解析委托。</summary>
        public Func<string, ISignatureVerifier> AsResolver() => Resolve;
    }
}
