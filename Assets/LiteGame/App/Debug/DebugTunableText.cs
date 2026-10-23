#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using System.Globalization;

namespace LiteGame
{
    /// <summary>调参文本格式/解析单源：控制台与运行期面板共用，Bool 用 on/off，Float 用 InvariantCulture。</summary>
    public static class DebugTunableText
    {
        public static string Format(DebugTunable tunable, float value)
        {
            if (tunable == null) return "-";
            if (tunable.Kind == DebugTunableKind.Bool) return value >= 0.5f ? "on" : "off";
            int decimals = tunable.Decimals < 0 ? 0 : tunable.Decimals;
            return value.ToString("F" + decimals, CultureInfo.InvariantCulture);
        }

        public static bool TryParse(DebugTunable tunable, string text, out float value, out string error)
        {
            value = 0f;
            error = null;
            if (tunable == null) { error = "调参项不存在"; return false; }
            if (tunable.Kind == DebugTunableKind.Bool)
            {
                switch ((text ?? string.Empty).Trim().ToLowerInvariant())
                {
                    case "on":
                    case "true":
                    case "1":
                        value = 1f;
                        return true;
                    case "off":
                    case "false":
                    case "0":
                        value = 0f;
                        return true;
                    default:
                        error = "Bool 值需为 on/off/true/false/1/0";
                        return false;
                }
            }

            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                error = "数值格式非法（InvariantCulture）";
                return false;
            }
            if (value < tunable.Min || value > tunable.Max)
            {
                error = $"超出范围 [{tunable.Min}, {tunable.Max}]";
                return false;
            }
            return true;
        }
    }
}
#endif
