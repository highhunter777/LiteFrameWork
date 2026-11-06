using System;

namespace LiteFramework
{
    /// <summary>
    /// 受信公钥集合（密钥轮换/撤销的最小落点）：按 keyId 查找公钥；**已撤销的 keyId 一律拒绝**
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
}
