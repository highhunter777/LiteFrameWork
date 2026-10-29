using System.Globalization;
using System.Text;

namespace LiteFramework.Diagnostics
{
    /// <summary>
    /// JSON 字符串转义单源（结构化日志 / 现场包共用）：控制字符走 \uXXXX，常用控制符走短转义。
    /// 两个出口的差别只在"要不要外围引号"——<see cref="AppendEscaped"/> 写内容，
    /// <see cref="AppendQuoted"/> 写带引号的完整 JSON 字符串。
    /// </summary>
    public static class JsonText
    {
        /// <summary>转义写入（不加引号；null/空串不写）。</summary>
        public static void AppendEscaped(StringBuilder sb, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
        }

        /// <summary>写带引号的完整 JSON 字符串（转义规则同 <see cref="AppendEscaped"/>；null = 空串）。</summary>
        public static void AppendQuoted(StringBuilder sb, string value)
        {
            sb.Append('"');
            AppendEscaped(sb, value ?? string.Empty);
            sb.Append('"');
        }
    }
}
