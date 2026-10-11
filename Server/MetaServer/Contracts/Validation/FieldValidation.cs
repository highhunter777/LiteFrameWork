using System.Collections.Generic;
using System.Text;

namespace MetaServer.Contracts.Validation
{
    /// <summary>
    /// 字段边界校验的**唯一实现**（P2 单源）：「非空（非空白）且 UTF-8 字节数 ≤ 上限」。
    ///
    /// **为什么要有它**：同一条规则此前在 Meta 里手写 6 份（GuestLogin / JoinTicketIssue / InstanceRegistry /
    /// ApplyMatchResult / MatchResultArchiveEntry / SampleSettlementCommand），并且**两份用了字符数、
    /// 四份用了 UTF-8 字节数**，错误文案还各写一套——同一纪律两种口径就等于没有纪律（多字节字符下
    /// 字符数与字节数不符，防超长串打爆日志/内存的目的直接落空）。
    ///
    /// **口径裁决**：一律按 **UTF-8 字节数**（服务端 §P0-3"解析后限制长度"的原意），
    /// 空值判定取 <see cref="string.IsNullOrWhiteSpace"/>（空白串等同空——"   " 不是有效标识）。
    /// 上限值见 <see cref="FieldLimits"/>（不在调用点就地写数字）。
    /// </summary>
    public static class FieldValidation
    {
        /// <summary>非空白且 UTF-8 字节数 ≤ <paramref name="maxBytes"/>（null/空串/纯空白/超界都算不合法）。</summary>
        public static bool IsValid(string value, int maxBytes)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            return Encoding.UTF8.GetByteCount(value) <= maxBytes;
        }

        /// <summary>
        /// 校验并**追加**错误（一次报全形态）：返回是否合法；不合法时向 <paramref name="errors"/> 追加一条。
        /// 文案单源：<c>"&lt;字段名&gt; 不得为空"</c> / <c>"&lt;字段名&gt; 不得超过 N UTF-8 字节"</c>
        /// ——只回显**字段名与上限**，不回显字段值（错误体进日志与客户端，字段值可能是敏感面）。
        /// </summary>
        public static bool Append(List<string> errors, string name, string value, int maxBytes)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add(name + " 不得为空");
                return false;
            }
            if (Encoding.UTF8.GetByteCount(value) > maxBytes)
            {
                errors.Add(name + " 不得超过 " + maxBytes + " UTF-8 字节");
                return false;
            }
            return true;
        }
    }
}
