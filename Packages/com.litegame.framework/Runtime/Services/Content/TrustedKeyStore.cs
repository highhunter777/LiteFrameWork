using System;
using System.Collections.Generic;
using LiteFramework;

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

        /// <summary>该 keyId 是否可用（已登记且未撤销）。</summary>
        public bool IsUsable(string keyId) => _ring.TryGet(keyId, out _);

        /// <summary>
        /// 按清单声明的 keyId 解析验签器。
        /// </summary>
        public ISignatureVerifier Resolve(string keyId)
            => RsaSignatureVerifier.FromKeyRing(_ring, keyId);

        /// <summary>供 <c>PatchRunner</c> 直接使用的解析委托。</summary>
        public Func<string, ISignatureVerifier> AsResolver() => Resolve;

        /// <summary>从信任清单 JSON 载入（轮换/撤销下发形态）。畸形条目跳过并计数，不整体失败。</summary>
        /// <returns>成功载入的条目数。</returns>
        public int LoadFromJson(string json, out int skipped)
        {
            skipped = 0;
            if (string.IsNullOrEmpty(json)) return 0;

            int loaded = 0;
            try
            {
                var root = Newtonsoft.Json.Linq.JObject.Parse(json);
                var keys = root["keys"] as Newtonsoft.Json.Linq.JArray;
                if (keys == null) return 0;

                foreach (var token in keys)
                {
                    try
                    {
                        string keyId = (string)token["keyId"];
                        string modulusB64 = (string)token["modulus"];
                        string exponentB64 = (string)token["exponent"];
                        bool revoked = (bool?)token["revoked"] ?? false;

                        if (string.IsNullOrEmpty(keyId) || string.IsNullOrEmpty(modulusB64)
                            || string.IsNullOrEmpty(exponentB64))
                        {
                            skipped++;
                            continue;
                        }

                        _ring.Add(keyId, Convert.FromBase64String(modulusB64),
                            Convert.FromBase64String(exponentB64), revoked);
                        loaded++;
                    }
                    catch (Exception)
                    {
                        skipped++;      // 单条畸形不拖垮整批（轮换期新旧并存，一条坏不该全废）
                    }
                }
            }
            catch (Exception)
            {
                return 0;               // 整体畸形 = 无可用信任，fail-closed
            }
            return loaded;
        }
    }
}
