using LiteFramework;
using LiteGame.View;
using NUnit.Framework;
using UnityEngine;
using LiteGame.Modules.Camera;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 相机视角端口（<see cref="ICameraService.AddLookDelta"/> / LookYaw / LookPitch）的契约测试：
    /// 增量累进、pitch 限位钳制（机制常量单源）、Reset 清视角态（硬切回默认）、
    /// ADS 翻转不重置视角、关闭后静默 no-op。状态层契约——vcam 应用面归场景 rig 改造与实机矩阵。
    /// </summary>
    public sealed class TpsCameraLookEditModeTests
    {
        private CinemachineCameraService _service;

        [SetUp]
        public void SetUp()
        {
            _service = new CinemachineCameraService();   // 无 vcam 场景：视角状态与相机解析无关（纯状态层）
        }

        [TearDown]
        public void TearDown()
        {
            _service?.Shutdown();
            _service = null;
        }

        [Test]
        public void 视角增量_累进yaw与pitch_零起()
        {
            Assert.AreEqual(0f, _service.LookYaw, 1e-5f, "未收增量 = 0");
            Assert.AreEqual(0f, _service.LookPitch, 1e-5f, "未收增量 = 0");

            _service.AddLookDelta(0.10f, 0.05f);
            _service.AddLookDelta(0.05f, 0.05f);
            Assert.AreEqual(0.15f, _service.LookYaw, 1e-5f, "yaw 增量累进");
            Assert.AreEqual(0.10f, _service.LookPitch, 1e-5f, "pitch 增量累进");
        }

        [Test]
        public void 视角增量_pitch超限钳在限位常量()
        {
            _service.AddLookDelta(0f, 10f);
            Assert.AreEqual(CinemachineCameraService.PitchClampMaxRad, _service.LookPitch, 1e-5f, "上仰越界钳上限");

            _service.AddLookDelta(0f, -10f);
            _service.AddLookDelta(0f, -10f);
            Assert.AreEqual(CinemachineCameraService.PitchClampMinRad, _service.LookPitch, 1e-5f, "下俯越界钳下限");
        }

        [Test]
        public void Reset_清视角态_硬切回默认视角()
        {
            _service.AddLookDelta(0.25f, 0.10f);
            _service.Reset();
            Assert.AreEqual(0f, _service.LookYaw, 1e-5f, "Reset 清 yaw（复活/传送硬切回默认视角）");
            Assert.AreEqual(0f, _service.LookPitch, 1e-5f, "Reset 清 pitch");
        }

        [Test]
        public void ADS翻转_视角状态保持_不重置()
        {
            _service.AddLookDelta(0.20f, 0.10f);
            _service.SetAiming(true);
            Assert.AreEqual(0.20f, _service.LookYaw, 1e-5f, "瞄准沿不重置视角（相机持续跟手）");
            _service.SetAiming(false);
            Assert.AreEqual(0.20f, _service.LookYaw, 1e-5f, "松开沿不重置视角");
        }

        [Test]
        public void Shutdown后_视角增量静默no_op()
        {
            _service.AddLookDelta(0.30f, 0.10f);
            float yawBefore = _service.LookYaw;
            _service.Shutdown();
            _service.AddLookDelta(0.40f, 0.10f);
            Assert.AreEqual(yawBefore, _service.LookYaw, 1e-5f, "关闭后不写视角状态（与'关闭后不写相机'同契约）");
        }

        [Test]
        public void 视角数学_POV轴角与恒定臂长俯仰弧_符号单源()
        {
            Assert.AreEqual(90f, TpsCameraMath.PovHorizontalDegrees(Mathf.PI * 0.5f), 1e-4f, "yaw 右转为正");
            Assert.AreEqual(-30f, TpsCameraMath.PovVerticalDegrees(30f * Mathf.Deg2Rad), 1e-4f,
                "上仰 → POV 垂直轴取负（POV 正 = 向下）");
            Assert.AreEqual(30f, TpsCameraMath.PovVerticalDegrees(-30f * Mathf.Deg2Rad), 1e-4f,
                "下俯 → POV 垂直轴为正");

            const float armY = 0.4f;
            const float armZ = -2.1f;
            Vector2 neutral = TpsCameraMath.PitchArm(0f, 0.4f, armY, armZ);
            Assert.AreEqual(armY, neutral.x, 1e-4f, "平视：高度不变");
            Assert.AreEqual(armZ, neutral.y, 1e-4f, "平视：后撤不变");

            Vector2 up = TpsCameraMath.PitchArm(30f * Mathf.Deg2Rad, 0.4f, armY, armZ);
            Assert.Less(up.x, armY, "上仰：相机降低");
            Assert.Less(up.y, armZ, "上仰：相机后撤");
            Assert.AreEqual(new Vector2(armY, armZ).magnitude, up.magnitude, 1e-4f, "上仰臂长守恒");

            Vector2 down = TpsCameraMath.PitchArm(-30f * Mathf.Deg2Rad, 0.4f, armY, armZ);
            Assert.Greater(down.x, armY, "下俯：相机抬高");
            Assert.Greater(down.y, armZ, "下俯：相机前收");
            Assert.AreEqual(up.magnitude, down.magnitude, 1e-4f, "俯仰两端臂长一致");

            Vector2 noFollow = TpsCameraMath.PitchArm(30f * Mathf.Deg2Rad, 0f, armY, armZ);
            Assert.AreEqual(armY, noFollow.x, 1e-4f, "follow=0：位置完全不跟俯仰");
            Assert.AreEqual(armZ, noFollow.y, 1e-4f);

            Vector3 view = TpsCameraMath.ViewDirection(30f * Mathf.Deg2Rad);
            Assert.AreEqual(0.5f, view.y, 1e-4f, "视线方向上仰分量");
            Assert.AreEqual(Mathf.Cos(30f * Mathf.Deg2Rad), view.z, 1e-4f, "视线方向前向分量");
        }
    }
}
