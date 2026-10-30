using System.Text;

namespace MetaWire
{
    /// <summary>
    /// 字段 UTF-8 字节上限（双端唯一真相，按**语义类别**归并而不是按模块）。
    /// 服务端 `FieldLimits` 是本表的别名引用；客户端据此裁剪，避免"注释说同值、代码没裁"。
    /// </summary>
    public static class MetaWireLimits
    {
        /// <summary>短标识：requestId / deviceId / accountId（1..128）。</summary>
        public const int ShortIdBytes = 128;

        /// <summary>业务键：matchId / playerId / instanceId / roomId / buildHash（1..64）。</summary>
        public const int BusinessKeyBytes = 64;

        /// <summary>可达地址 <c>host:port</c>（1..128）。</summary>
        public const int AddressBytes = 128;

        /// <summary>幂等操作标识 operationId（1..256；允许带前缀的复合键）。</summary>
        public const int OperationIdBytes = 256;

        /// <summary>
        /// 按 UTF-8 字节上限裁剪（不拆代理对；超限即截到最后一个完整字符）。
        /// 服务端按字节校验、客户端必须按同一口径裁剪，否则长 deviceId 会被 400 拒绝。
        /// </summary>
        public static string TrimUtf8(string value, int maxBytes)
        {
            if (string.IsNullOrEmpty(value) || maxBytes <= 0) return string.Empty;
            if (Encoding.UTF8.GetByteCount(value) <= maxBytes) return value;

            int bytes = 0;
            for (int i = 0; i < value.Length; i++)
            {
                int size;
                if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length
                    && char.IsLowSurrogate(value[i + 1]))
                {
                    size = 4;
                }
                else if (value[i] < 0x80) size = 1;
                else if (value[i] < 0x800) size = 2;
                else size = 3;

                if (bytes + size > maxBytes) return value.Substring(0, i);
                bytes += size;
                if (size == 4) i++;
            }
            return value;
        }
    }
}
