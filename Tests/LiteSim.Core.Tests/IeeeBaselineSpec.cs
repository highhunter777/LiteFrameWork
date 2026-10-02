using System;

namespace LiteSim.Tests
{
    /// <summary>
    /// IEEE 边界基线规格（《M7 实施指导》§2.6）——**委托给 <see cref="LiteSim.IeeeProbe"/>**。
    ///
    /// 探针（样本 / 运算 / 链 checksum）位于 `LiteSim.Core`——Unity 侧对账（Editor 菜单 + EditMode 用例）
    /// 与测试侧共用**同一批输入与同一套运算**，两侧各写一份必然漂移。
    /// 本类只保留测试侧的取用入口（基线文本逐字节不变）。
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
