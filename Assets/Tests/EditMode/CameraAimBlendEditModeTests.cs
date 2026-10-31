using System.Collections.Generic;
using LiteGame;
using NUnit.Framework;
using UnityEngine;
using LiteGame.Modules.Camera;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 相机瞄准混合门用例（ADS 相机抖动治理——频繁点按不再重启相机混合的输入侧语义）。
    /// 纯 C# 确定性状态机（dt 全部注入、不读时钟）；**黄金值走生产调用链**：
    /// <see cref="CameraAimBlendDriver"/> → <see cref="ICameraService.SetAiming"/>，不直接断言私有常量。
    /// 宽度取相机域单源 <see cref="CameraBlendDefaults.AimBlendSeconds"/>（Brain DefaultBlend 0.3s）。
    /// </summary>
    public sealed class CameraAimBlendEditModeTests
    {
        private const float Hold = CameraBlendDefaults.AimBlendSeconds;

        /// <summary>只记录 <see cref="SetAiming"/> 的相机端口替身（其余成员为空实现）。</summary>
        private sealed class RecordingCamera : ICameraService
        {
            public readonly List<bool> Aiming = new List<bool>();
            public bool HasFocus => false;
            public Vector3 Focus => Vector3.zero;
            public float LookYaw => 0f;
            public float LookPitch => 0f;
            public void Follow(in Vector3 target, in Quaternion facing, float deltaSeconds) { }
            public void SetAiming(bool aiming) => Aiming.Add(aiming);
            public void SetAimPoint(in Vector3 point, bool hasPoint) { }
            public void ToggleShoulder() { }
            public void AddLookDelta(float yawDelta, float pitchDelta) { }
            public void Reset() { }
            public bool EnsureReady() => true;
            public void Shutdown() { }
        }

        [Test]
        public void 短按_释放推迟到满宽度才落()
        {
            var gate = new CameraAimBlendGate(Hold);

            Assert.IsTrue(gate.Feed(true, 0.05f), "上升沿立即生效");
            Assert.IsTrue(gate.Feed(false, 0.1f), "短按（0.05s < 0.3s）：进入推迟窗口");
            Assert.IsTrue(gate.Feed(false, 0.1f), "窗口倒计时中维持 1");
            Assert.IsTrue(gate.Feed(false, 0.1f), "窗口倒计时中维持 2");
            Assert.IsFalse(gate.Feed(false, 0.1f), "窗口走完释放（距上升沿满 0.3s）");
        }

        [Test]
        public void 长按_释放立即生效_再短按重新得完整窗口()
        {
            var gate = new CameraAimBlendGate(Hold);

            Assert.IsTrue(gate.Feed(true, 1.0f), "长按瞄准");
            Assert.IsFalse(gate.Feed(false, 0.016f), "本轮按下持续 ≥ 宽度：释放当帧立即生效（长按手感不变）");

            Assert.IsTrue(gate.Feed(true, 0.05f), "再按：上升沿立即生效（按持续时长已清零重计）");
            Assert.IsTrue(gate.Feed(false, 0.1f), "再短按仍得完整推迟窗口");
            Assert.IsTrue(gate.Feed(false, 0.2f), "窗口尾段仍维持");
            Assert.IsFalse(gate.Feed(false, 0.1f), "窗口走完释放");
        }

        [Test]
        public void 连点_瞄准持续不翻转_点按停止后窗口释放()
        {
            var gate = new CameraAimBlendGate(Hold);

            for (int i = 0; i < 30; i++)
            {
                Assert.IsTrue(gate.Feed(true, 0.02f), $"第 {i} 次点按：上升沿生效");
                Assert.IsTrue(gate.Feed(false, 0.05f), $"第 {i} 次点按的间隔：推迟窗口内不翻转（相机无重启混合的机会）");
            }

            bool released = false;
            float elapsed = 0f;
            while (elapsed < Hold + 0.1f)
            {
                if (!gate.Feed(false, 0.05f)) { released = true; break; }
                elapsed += 0.05f;
            }
            Assert.IsTrue(released, "点按停止后，最后一个短按的推迟窗口走完应释放");
        }

        [Test]
        public void 推迟窗口内再按_取消推迟重新武装()
        {
            var gate = new CameraAimBlendGate(Hold);

            Assert.IsTrue(gate.Feed(true, 0.05f));
            Assert.IsTrue(gate.Feed(false, 0.1f), "短按进入推迟窗口");

            Assert.IsTrue(gate.Feed(true, 0.02f), "窗口内再按：取消推迟、维持瞄准");
            Assert.IsTrue(gate.Feed(false, 0.02f), "再按后的释放：重新进入推迟（按持续时长重新计算）");
            Assert.IsTrue(gate.Feed(false, 0.1f), "新窗口内维持");
            Assert.IsFalse(gate.Feed(false, 0.2f), "新窗口走完释放");
        }

        [Test]
        public void 零宽度_退化为直通()
        {
            var gate = new CameraAimBlendGate(0f);

            Assert.IsTrue(gate.Feed(true, 1f));
            Assert.IsFalse(gate.Feed(false, 1f), "宽度 0：释放当帧立即生效（门控退化为透传）");
        }

        /// <summary>
        /// 黄金值（生产路径）：固定输入序列，经 <see cref="CameraAimBlendDriver"/> 观察
        /// <see cref="ICameraService.SetAiming"/> 的翻转帧。改名前由旧门在同一序列下录制；
        /// 改名/把 0.3s 常量移入相机域后同一断言不改仍绿。
        ///
        /// 序列（dt = 0.1s）：第 0~1 帧按住、第 2 帧起松开 → 第 4 帧落；第 10 帧再按 1 帧 → 第 14 帧落。
        /// </summary>
        [Test]
        public void 黄金值_固定输入序列_SetAiming翻转时刻保持()
        {
            var camera = new RecordingCamera();
            var driver = new CameraAimBlendDriver(camera, CameraBlendDefaults.AimBlendSeconds);
            var flips = new List<int>();
            bool previous = false;

            // dt 固定 0.1f：黄金翻转帧依赖该采样步长；只钉翻转序列，不把“每帧重复下发”固化成契约。
            for (int frame = 0; frame < 30; frame++)
            {
                bool rawAiming = frame < 2 || (frame >= 10 && frame < 11);   // 两次点按（各 1~2 帧）
                driver.Tick(rawAiming, 0.1f);
                bool now = camera.Aiming[camera.Aiming.Count - 1];
                if (now != previous) { flips.Add(frame); previous = now; }
            }

            CollectionAssert.AreEqual(new[] { 0, 4, 10, 14 }, flips,
                "相机混合翻转时刻（生产调用链的黄金值）");
        }
    }
}
