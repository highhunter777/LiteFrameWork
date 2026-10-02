using LiteSim.View;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>瞄准机构图 z 偏移曲线（纯二次）：端点连续与"Z &lt; d"性质。</summary>
    public sealed class AimOffsetCurveEditModeTests
    {
        [Test]
        public void 曲线_过原点_基值处连续_超出不干预()
        {
            Assert.AreEqual(0f, AimOffsetCurve.Z(0f, 5f), 1e-5f, "鼠标贴到玩家 → 偏移归零");
            Assert.AreEqual(5f, AimOffsetCurve.Z(5f, 5f), 1e-5f, "d=基值 → 接回场景值");
            Assert.AreEqual(5f, AimOffsetCurve.Z(8f, 5f), 1e-5f, "超出基值不干预");
        }

        [Test]
        public void 曲线_全程Z小于距离_1米5处为纯二次值()
        {
            Assert.AreEqual(0.45f, AimOffsetCurve.Z(1.5f, 5f), 1e-4f, "1.5m 处 Z=d²/5=0.45 < 1.5");
            for (float d = 0.1f; d < 5f; d += 0.1f)
                Assert.Less(AimOffsetCurve.Z(d, 5f), d, $"d={d} 时 Z 必须小于距离");
        }

        [Test]
        public void 曲线_单调不减_基值非法回零()
        {
            float previous = -1f;
            for (float d = 0f; d <= 6f; d += 0.05f)
            {
                float z = AimOffsetCurve.Z(d, 4f);
                Assert.GreaterOrEqual(z, previous, $"d={d} 处应单调不减");
                previous = z;
            }
            Assert.AreEqual(0f, AimOffsetCurve.Z(2f, 0f), 1e-5f, "基值 ≤0（配置无效）→ 回 0");
        }
    }
}