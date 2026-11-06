using System;

namespace LiteSim.Tests
{
    /// <summary>
    /// IEEE 边界基线规格（《M7 实施指导》§2.6）——**委托给 <see cref="LiteSim.IeeeProbe"/>**。
    /// </summary>
    public static class IeeeBaselineSpec
    {
        public const string Header = LiteSim.IeeeProbe.Header;

        /// <summary>输入样本（含负数、±0、次正规、极值、精度敏感小数）。</summary>
        public static readonly float[] Values = LiteSim.IeeeProbe.Values;

        /// <summary>数据行（不含表头）：Sqrt 逐值 + 相邻对 Add/Sub/Mul/Div + Chain checksum。</summary>
        public static string[] BuildLines() => LiteSim.IeeeProbe.BuildLines();

        public static string BuildText() => LiteSim.IeeeProbe.BuildText();

        /// <summary>运算链 checksum：SimVector3 混合运算 10k 步 + SimRng 序列 → uint。</summary>
        public static uint ChainChecksum() => LiteSim.IeeeProbe.ChainChecksum();
    }
}
