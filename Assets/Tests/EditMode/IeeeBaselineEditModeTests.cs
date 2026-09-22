using LiteSim.Editor;
using LiteTesting;
using LiteTesting.Unity;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// Unity 侧 IEEE 基线对账（《测试开发方案》§7.2 缺口 b / §9 验收②③；《M7 实施指导》§2.6）。
    ///
    /// 断言"**Unity 运行时与 .NET 的浮点路径逐位一致**"——这是跨运行时一致性的直接证据，
    /// 也是 L2 门禁里"数值层"那一环（v3 定位下它不再是正确性前提，但决定预测/和解质量）。
    /// 探针与基线均为单一来源：<see cref="IeeeProbe"/>（LiteSim 内）+ `Tests/…/Baselines/IeeeBaseline.txt`（.NET 记录）。
    /// </summary>
    public sealed class IeeeBaselineEditModeTests : UnityTestBase
    {
        [Test]
        [Category(TestCategory.Contract)]
        public void IEEE基线_Unity侧与NET侧逐位一致()
        {
            var (ok, report) = IeeeBaselineChecker.Verify();
            Assert.IsTrue(ok, report);
        }

        /// <summary>
        /// 运算链（10k 步）跨运行时**必须逐位一致**——B 方案（自研 software sqrt）的兑现点。
        ///
        /// 历史：2026-09-18 曾实测到不一致（.NET 2896875742 / Unity 3683559206），
        /// 根因是 BCL `Math.Sqrt` 在 Mono 上非正确舍入、被长链放大；改为自研 software sqrt
        /// （纯整数/位运算 + 正确舍入）后，两侧一致。此用例自此为**硬判据**：
        /// 一旦回归（例如有人绕过 `SimMath.Sqrt` 直接用 BCL），它立刻红。
        /// </summary>
        [Test]
        [Category(TestCategory.Contract)]
        public void IEEE运算链_跨运行时逐位一致()
        {
            var (expected, unity, same) = IeeeBaselineChecker.ChainComparison();
            Assert.IsTrue(same,
                $"运算链跨运行时不一致：.NET={expected} / Unity={unity}——" +
                "检查是否有人绕过 SimMath.Sqrt 用了 BCL Math.Sqrt（B 方案红线）");
        }
    }
}
