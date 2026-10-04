using LiteGame;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 瞄准态滞回门用例（2026-10-04 ADS 相机抖动治理——频繁点按不再重启相机混合的输入侧语义）。
    /// 纯 C# 确定性状态机：dt 全部注入（不读时钟），逐帧脚本化喂入。
    /// 宽度取 0.3s 断言（与生产常量 AimMinHoldSeconds 同值—— Brain DefaultBlend 时长）。
    /// </summary>
    public sealed class AimHoldGateEditModeTests
    {
        private const float Hold = 0.3f;

        [Test]
        public void 短按_释放推迟到满宽度才落()
        {
            var gate = new AimHoldGate(Hold);

            Assert.IsTrue(gate.Feed(true, 0.05f), "上升沿立即生效");
            Assert.IsTrue(gate.Feed(false, 0.1f), "短按（0.05s < 0.3s）：进入推迟窗口");
            Assert.IsTrue(gate.Feed(false, 0.1f), "窗口倒计时中维持 1");
            Assert.IsTrue(gate.Feed(false, 0.1f), "窗口倒计时中维持 2");
            Assert.IsFalse(gate.Feed(false, 0.1f), "窗口走完释放（距上升沿满 0.3s）");
        }

        [Test]
        public void 长按_释放立即生效_再短按重新得完整窗口()
        {
            var gate = new AimHoldGate(Hold);

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
            var gate = new AimHoldGate(Hold);

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
            var gate = new AimHoldGate(Hold);

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
            var gate = new AimHoldGate(0f);

            Assert.IsTrue(gate.Feed(true, 1f));
            Assert.IsFalse(gate.Feed(false, 1f), "宽度 0：释放当帧立即生效（门控退化为透传）");
        }
    }
}
