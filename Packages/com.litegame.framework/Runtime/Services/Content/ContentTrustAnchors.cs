using System;
using System.Collections.Generic;
using LiteFramework;

namespace LiteFramework
{
    /// <summary>
    /// 内置信任锚表（《热更与内容发布专项设计》§6"内置锚点 + 可选外部撤销清单"）： **根信任编入应用本体**（随应用签名分发——不放可写存储，否则攻击者加入自己的公钥 + 自签候选即绕过整条验签链）。
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
        /// 内置锚点（Base64 = <see cref="TrustedKeyStore.LoadFromJson"/> 同款编码；
        /// 条目以 <c>yield return new Anchor(...)</c> 形式追加）。
        /// </summary>
        public static IEnumerable<Anchor> BuiltIn
        {
            get
            {
                yield return new Anchor(
                    "release-key-2026-09-26",
                    Convert.FromBase64String("tgkOQKLKzUvND2kruLTjIRRDXWQVAiQ21Wr5eI+VINAtJwnai0CS9qFf8nZQozA5rgPYDAYlULHC95p4+/pslvIXL7PY5Dx7rOe2tlmUUygyah5dvQOPZkz07EigiVxfy8ZkwCOahA+Ptr43W9MQ8OMEMQW0CeCkeO/jRhqgPqsYyBsl5jch9S9DdoOpA9ngjPcp6pO61nCrCTUulNOz8DinbpF8xPR5bBLmKsFjZ0XGas4R3EDdzx0AYLeZ2bdrfQny+nJ30vmIESlB58VMXXigAbHQO6rmqCDoE3LDIHQjvamRalmGivU4x7TrAFAM0Rq0bqLg46thpcMxomhK5Q=="),
                    Convert.FromBase64String("AQAB"));   // e = 65537
            }
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
