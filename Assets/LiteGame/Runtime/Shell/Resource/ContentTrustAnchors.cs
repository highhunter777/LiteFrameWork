using System.Collections.Generic;
using LiteFramework;

namespace LiteGame
{
    /// <summary>
    /// 内置信任锚表（《热更与内容发布专项设计》§6"内置锚点 + 可选外部撤销清单"）：
    /// **根信任编入应用本体**（随应用签名分发——不放可写存储，否则攻击者加入自己的公钥
    /// + 自签候选即绕过整条验签链）。
    ///
    /// **生成契约**：本文件由发布流程从信任清单生成（私钥留在签名机，**绝不入库/入包**）；
    /// 当前为**零锚点**——任何候选签名都会因"keyId 未登记"被拒（fail-closed 保持，§8）。
    /// 锚点 provisioning = 运维生成密钥对 → 公钥条目加入信任清单 → 重新生成本文件 → 随包发布。
    ///
    /// 轮换：新旧 keyId 并存（Add 两次），旧版撤销经 <see cref="TrustedKeyStore.Revoke"/>
    /// （由下一版内置表体现——撤销清单随可写存储分发会被篡改，首版不做）。
    /// </summary>
    public static class ContentTrustAnchors
    {
        /// <summary>内置锚点条目。</summary>
        public readonly struct Anchor
        {
            public readonly string KeyId;
            public readonly byte[] Modulus;
            public readonly byte[] Exponent;

            public Anchor(string keyId, byte[] modulus, byte[] exponent)
            {
                KeyId = keyId;
                Modulus = modulus;
                Exponent = exponent;
            }
        }

        /// <summary>
        /// 内置锚点（当前为零——见生成契约）。条目以 <c>yield return new Anchor(...)</c> 形式追加。
        /// </summary>
        public static IEnumerable<Anchor> BuiltIn
        {
            get { yield break; }
        }

        /// <summary>把内置锚点登记进受信公钥库（装配点调用；返回登记数——0 = 无锚点，fail-closed）。</summary>
        public static int ApplyTo(TrustedKeyStore store)
        {
            if (store == null) throw new System.ArgumentNullException(nameof(store));
            int n = 0;
            foreach (Anchor a in BuiltIn)
            {
                store.Add(a.KeyId, a.Modulus, a.Exponent);
                n++;
            }
            return n;
        }
    }
}
