using System.Globalization;

namespace LiteSim.View.DamageNumbers
{
    /// <summary>
    /// 伤害数字文本格式化（位数/K 简写——《命中反馈与伤害数字专项设计》§4.2；DNP ProcessIntegers
    /// 语义自研对应物）。**InvariantCulture 定小数点**：跨区域设置一致（德/土语区小数逗号不进对局表现）。
    /// </summary>
    public static class DamageNumberFormatter
    {
        /// <summary>≥10⁴ 才简写（当前对局伤害量级内多为原位数；简写走"15K"读感）。</summary>
        public const int KThreshold = 10000;

        public static string Format(int value)
        {
            if (value < KThreshold)
                return value.ToString(CultureInfo.InvariantCulture);

            float k = value / 1000f;
            // 整千不带小数（20K 而非 20.0K）；带余一位小数（12.3K）
            return (k % 1f == 0f
                    ? ((int)k).ToString(CultureInfo.InvariantCulture)
                    : k.ToString("F1", CultureInfo.InvariantCulture))
                + "K";
        }
    }
}
