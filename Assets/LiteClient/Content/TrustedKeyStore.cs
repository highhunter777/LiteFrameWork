using System;
using System.Collections.Generic;
using LiteFramework;

namespace LiteClient
{
    /// <summary>
    /// 受信公钥库（《热更与内容发布专项设计》§6"客户端只带信任公钥，私钥由受控发布签名环境持有"、
    /// "必须定义根信任、密钥轮换/撤销"）。
    ///
    /// **信任根的形态决策**：内置锚点（编译进包）+ 可选的外部撤销清单。
    /// 不采用"完全远程可更新的信任根"——那需要信任根自己签名自己（先有鸡还是先有蛋）。
    /// 轮换靠**同时内置新旧两把公钥**，用 <see cref="TrustedKeyRing.Revoke"/> 停止接受旧签名；
    /// 撤销清单随内容发布下发（它本身不构成新信任，只能**收回**既有信任）。
    ///
    /// **当前状态**：未配置任何内置锚点时 <see cref="ProbeCount"/> 为 0，
    /// 所有候选都会因"无可用的验签器"被拒——这是刻意的 fail-closed，
    /// 与"无健康探针即判不健康"同款纪律。生产锚点由发布流水线的密钥生成产物填入。
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
        /// **未登记或已撤销 → null**，调用方据此拒绝候选（<see cref="ReleaseManifestValidator"/>
        /// 会把 null 判为 <see cref="ReleaseRejectReason.UnknownOrRevokedKey"/>）。
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
