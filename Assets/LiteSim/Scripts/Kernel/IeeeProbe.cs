using System;
using System.Collections.Generic;
using System.Text;

namespace LiteSim
{
    /// <summary>
    /// IEEE 边界探针（《M7 实施指导》§2.6 / §9 验收②③）——**跨运行时对账的唯一真值源**。
    ///
    /// 为什么住在 LiteSim 而不是测试工程：.NET 侧（`Tests/LiteSim.Core.Tests/IeeeBaselineSpec`）与
    /// Unity 侧（Editor 对账菜单 / EditMode 用例）必须用**同一批输入、同一套运算**逐位比较——
    /// 各写一份必然漂移（探针本身成了不确定性来源）。故数据与算法只此一份，两侧都调它。
    ///
    /// 对账内容：`Sqrt` 逐值 + 相邻对 `Add/Sub/Mul/Div` + 10k 步运算链 checksum（全部只用 IEEE 基本运算，
    /// 逐位比较 <see cref="BitConverter.SingleToInt32Bits"/>，非容差）。
    /// </summary>
    public static class IeeeProbe
    {
        public const string Header =
            "# IEEE 边界基线 — .NET 侧逐位记录（BitConverter.SingleToInt32Bits）\n" +
            "# 布局：<OP> <argBits...> <resultBits>；逐位比较，非容差。\n" +
            "# 覆盖：+0/-0、次正规、极值、精度敏感小数。\n";

        /// <summary>输入样本（含负数、±0、次正规、极值、精度敏感小数）。</summary>
        public static readonly float[] Values =
        {
            0f, -0f, 1f, -1f, 0.5f, -0.5f,
            2f, -2f, 0.1f, 0.2f, 0.3f, 0.7f,
            1e-45f,          // 最小次正规
            1.1754944e-38f,  // 最小正规
            3.4028235e38f,   // 最大
            -3.4028235e38f,
            1e30f, -1e30f, 1e-30f, -1e-30f,
            1e5f, -1e5f, 123.456f, -123.456f,
        };

        /// <summary>数据行（不含表头）：Sqrt 逐值 + 相邻对 Add/Sub/Mul/Div + Chain checksum。</summary>
        public static string[] BuildLines()
        {
            var lines = new List<string>();

            for (int i = 0; i < Values.Length; i++)
            {
                lines.Add("Sqrt " + Bits(Values[i]) + " " + Bits(SimMath.Sqrt(Values[i])));
            }

            for (int i = 0; i + 1 < Values.Length; i++)
            {
                float a = Values[i];
                float b = Values[i + 1];
                lines.Add("Add " + Bits(a) + " " + Bits(b) + " " + Bits(a + b));
                lines.Add("Sub " + Bits(a) + " " + Bits(b) + " " + Bits(a - b));
                lines.Add("Mul " + Bits(a) + " " + Bits(b) + " " + Bits(a * b));
                lines.Add("Div " + Bits(a) + " " + Bits(b) + " " + Bits(a / b));
            }

            lines.Add("Chain " + ChainChecksum());
            return lines.ToArray();
        }

        /// <summary>完整基线文本（表头 + 数据行）——两侧逐字节比较此文本。</summary>
        public static string BuildText()
        {
            var sb = new StringBuilder();
            sb.Append(Header);
            string[] lines = BuildLines();
            for (int i = 0; i < lines.Length; i++) sb.Append(lines[i]).Append('\n');
            return sb.ToString();
        }

        /// <summary>
        /// 运算链 checksum：SimVector3 混合运算 10k 步 + SimRng 序列 → uint。
        /// 只用 IEEE 基本运算与 SimMath.Sqrt，跨运行时逐位确定（等价 §9 验收③）。
        /// </summary>
        public static uint ChainChecksum()
        {
            var rng = new SimRng(0x0123456789ABCDEFUL);
            var v = new SimVector3(0.1f, 0.2f, 0.3f);
            uint h = 2166136261u;

            for (int i = 0; i < 10000; i++)
            {
                float a = rng.NextFloat01();
                var d = new SimVector3(a, a * 2f, 0f - a);
                v = (v + d) * 0.5f;
                uint bits = (uint)BitConverter.SingleToInt32Bits(v.Length);
                h = (h ^ bits) * 16777619u;
            }
            return h;
        }

        private static string Bits(float v)
        {
            return BitConverter.SingleToInt32Bits(v).ToString();
        }
    }
}
