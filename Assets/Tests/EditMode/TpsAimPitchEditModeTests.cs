using LiteGame.View.Animation;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>俯仰姿态映射契约（程序化 aim pitch）：相机 pitch → 上半身姿态角——
    /// 区间内恒等（所见即所俯仰）、越界钳在机制常量限位（素材批落地后退役为素材驱动）。</summary>
    public sealed class TpsAimPitchEditModeTests
    {
        [Test]
        public void 俯仰映射_区间内恒等_越界钳制()
        {
            Assert.AreEqual(0.3f, AimPitchPosture.UpperBodyPitch(0.3f), 1e-4f, "区间内恒等映射");
            Assert.AreEqual(AimPitchPosture.MaxPitchRad, AimPitchPosture.UpperBodyPitch(10f), 1e-4f, "上仰越界钳上限");
            Assert.AreEqual(AimPitchPosture.MinPitchRad, AimPitchPosture.UpperBodyPitch(-10f), 1e-4f, "下俯越界钳下限");
            Assert.AreEqual(0f, AimPitchPosture.UpperBodyPitch(0f), 1e-4f, "水平相机零姿态");
        }
    }
}
