using System.Collections.Generic;
using System.Text;

namespace MetaServer.Contracts.Validation
{
    /// <summary>
    /// 字段边界校验的**唯一实现**（P2 单源）：「非空（非空白）且 UTF-8 字节数 ≤ 上限」。
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
