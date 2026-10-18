using LiteNet;
using LiteTesting.Unity;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 对局诊断读口的 L2 EditMode 覆盖（联机诊断 HUD 批）：钉住描述单源的展示口径——
    /// 丢包千分比 → 一位小数百分比（与 BattleNetworkHudDriver 同口径）、无样本占位不显示数值、
    /// 无对局空态（DescribeCurrent = null ⇒ 消费方整组隐藏，不显示陈旧值）。
    /// 登记口（Attach/Detach）接在对局视图装配/拆视图两处，EditMode 不造 BattleContext 假体——归 Play 面核证。
    /// </summary>
    public sealed class BattleDiagnosticsEditModeTests : UnityTestBase
    {
        [Test]
        public void 诊断行_有样本_千分比转一位小数百分比()
        {
            var snapshot = new BattleDiagnostics.Snapshot(
                ClientSessionPhase.Connected, new NetworkQuality(48, 3, true), 12);
            string[] lines = BattleDiagnostics.DescribeLines(snapshot);

            Assert.AreEqual(2, lines.Length);
            Assert.AreEqual("相位: Connected  RTT: 48ms  丢包: 0.3%", lines[0], "3‰ = 0.3%（生产 HUD 同口径）");
            Assert.AreEqual("和解: 12 次", lines[1]);
        }

        [Test]
        public void 诊断行_无样本_占位不显示数值()
        {
            var snapshot = new BattleDiagnostics.Snapshot(
                ClientSessionPhase.Reconnecting, NetworkQuality.None, 0);
            string[] lines = BattleDiagnostics.DescribeLines(snapshot);

            Assert.AreEqual("相位: Reconnecting  RTT: 无样本  丢包: —", lines[0], "无样本占位不撒谎");
            Assert.AreEqual("和解: 0 次", lines[1]);
        }

        [Test]
        public void 无对局_DescribeCurrent为空_消费方整组隐藏()
        {
            Assert.IsNull(BattleDiagnostics.DescribeCurrent(), "无对局 = null（HUD 段/GM 页读数按空态隐藏）");
        }
    }
}
