using LiteFramework.Animation;
using LiteView.Animation;
using LiteTesting;
using LiteTesting.Unity;
using NUnit.Framework;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 直 Clip 后端（《动画模块专项设计》§4「Clip 资源键」分支——控制器可选）：
    /// 无 RuntimeAnimatorController 的模型（带 Animator、片段经 RegisterClip 外部登记）必须完整可用——
    /// 单片段直驱、完成边界、混合节点/就地调参、能力位诚实声明、默认姿态位缺席时的诊断口径。
    /// 片段为程序化构造（SetCurve 定长），不依赖任何美术资产。
    /// </summary>
    public sealed class ClipDirectBackendEditModeTests : UnityTestBase
    {
        private static AnimationClip Clip(string name, float seconds)
        {
            var clip = new AnimationClip { name = name };
            clip.SetCurve("", typeof(Transform), "localPosition.x",
                new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(seconds, 1f)));
            Assert.Greater(clip.length, 0f, "程序化片段须有可判定的时长");
            return clip;
        }

        private (GameObject go, AnimatorAnimationBackend backend) Build()
        {
            var go = Scope.CreateGameObject("anim", typeof(Animator));
            var animator = go.GetComponent<Animator>();
            Assert.IsNull(animator.runtimeAnimatorController, "前置：直 Clip 形态——无控制器资产");
            var backend = new AnimatorAnimationBackend(animator, blendSeconds: 0f);
            return (go, backend);
        }

        private static AnimationResolvedPlayback Playback(string binding, AnimationChannel channel,
            bool loop = false, float speed = 1f)
            => new AnimationResolvedPlayback(new AnimationId("Test." + binding), channel, binding,
                0f, speed, requiresLoad: false, loop);

        // ---- 构造与能力位 ----

        [Test]
        [Category(TestCategory.Contract)]
        public void 无控制器可构造_能力位诚实_无默认姿态位()
        {
            var (_, backend) = Build();

            var caps = backend.Capabilities;
            Assert.IsTrue(caps.HasFlag(AnimationBackendCapabilities.ClipBlending), "Clip 直驱：混合能力在");
            Assert.IsTrue(caps.HasFlag(AnimationBackendCapabilities.Looping | AnimationBackendCapabilities.StartAtNormalized
                | AnimationBackendCapabilities.SpeedOverride), "循环/起点/倍率能力在");
            Assert.IsTrue(caps.HasFlag(AnimationBackendCapabilities.OverrideChannel),
                "覆盖层不需 Mask——任何 rig 声明 OverrideChannel（分层模式）");
            Assert.IsFalse(caps.HasFlag(AnimationBackendCapabilities.OverlayChannel),
                "非 humanoid 无 Mask——OverlayChannel 不声明（不静默降级）");

            Assert.IsTrue(backend.TryGetChannelDebug(AnimationChannel.Base, out var debug));
            Assert.AreEqual("none", debug.Source, "无控制器 → 无默认姿态位（诊断不报 controller）");
            Assert.IsFalse(backend.IsChannelActive(AnimationChannel.Base));
        }

        // ---- 单片段直驱与完成边界 ----

        [Test]
        [Category(TestCategory.Contract)]
        public void RegisterClip外驱_单片段播放_推进到自然完成()
        {
            var (_, backend) = Build();
            backend.RegisterClip("hit", Clip("hit", 1f));

            Assert.IsTrue(backend.TryPlay(Playback("hit", AnimationChannel.Override)));
            Assert.IsTrue(backend.IsChannelActive(AnimationChannel.Override));

            Assert.AreEqual(AnimationChannelMask.None, backend.Tick(0.5f), "1 秒片段走到一半：未完成");
            Assert.AreEqual(AnimationChannelMask.Override, backend.Tick(0.6f), "越过结束边界：Override 自然完成");

            Assert.IsTrue(backend.TryGetChannelDebug(AnimationChannel.Override, out var debug));
            Assert.AreEqual("hit", debug.Source);
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 循环定义_不产生自然完成()
        {
            var (_, backend) = Build();
            backend.RegisterClip("walk", Clip("walk", 0.5f));

            Assert.IsTrue(backend.TryPlay(Playback("walk", AnimationChannel.Base, loop: true)));

            for (int i = 0; i < 6; i++)
                Assert.AreEqual(AnimationChannelMask.None, backend.Tick(0.3f),
                    "循环定义恒不自然 Completed（判定归定义，不读资产）");
            Assert.IsTrue(backend.IsChannelActive(AnimationChannel.Base));
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 未登记绑定_显性拒绝并计数()
        {
            var (_, backend) = Build();

            Assert.IsFalse(backend.TryPlay(Playback("ghost", AnimationChannel.Override)), "未登记绑定：拒绝不假装在播");
            Assert.AreEqual(1, backend.UnknownBindings);
            Assert.IsFalse(backend.TryPlay(Playback("ghost", AnimationChannel.Override)));
            Assert.AreEqual(2, backend.UnknownBindings, "每次拒绝都计数（可观测）");
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 无Mask时Overlay通道显性拒绝()
        {
            var (_, backend) = Build();
            backend.RegisterClip("aim", Clip("aim", 1f));

            Assert.IsFalse(backend.TryPlay(Playback("aim", AnimationChannel.Overlay)),
                "非 humanoid 无 Mask：Overlay 显性拒绝（不静默降级）");
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 不分层构造_仅基础通道_其余通道显性拒绝()
        {
            var go = Scope.CreateGameObject("anim", typeof(Animator));
            var backend = new AnimatorAnimationBackend(go.GetComponent<Animator>(), blendSeconds: 0f, layered: false);
            backend.RegisterClip("idle", Clip("idle", 1f));
            backend.RegisterClip("die", Clip("die", 1f));

            // 能力位诚实：分层两位均不声明、不构造叠加层 Mask（省骨架遍历）
            var caps = backend.Capabilities;
            Assert.IsFalse(caps.HasFlag(AnimationBackendCapabilities.OverrideChannel), "不分层：覆盖层能力位不声明");
            Assert.IsFalse(caps.HasFlag(AnimationBackendCapabilities.OverlayChannel), "不分层：叠加层能力位不声明");
            Assert.IsFalse(backend.HasOverlayMask, "不分层：不构造叠加层 Mask");

            // 基础通道照常：直驱 + 自然完成（完成掩码只含 Base）
            Assert.IsTrue(backend.TryPlay(Playback("idle", AnimationChannel.Base, loop: true)));
            Assert.IsTrue(backend.TryPlay(Playback("die", AnimationChannel.Base)));
            Assert.IsTrue(backend.IsChannelActive(AnimationChannel.Base));
            Assert.AreEqual(1, backend.ActiveChannels, "不分层：仅基础通道可占用");
            Assert.AreEqual(AnimationChannelMask.Base, backend.Tick(1.2f), "一次性越过边界——完成掩码只含 Base");

            // 非基础通道显性拒绝（不分层图无该通道层——未知通道口径）
            Assert.IsFalse(backend.TryPlay(Playback("die", AnimationChannel.Override)), "不分层：Override 显性拒绝");
            Assert.IsFalse(backend.TryPlay(Playback("die", AnimationChannel.Overlay)), "不分层：Overlay 显性拒绝");
        }

        // ---- 混合节点与就地调参 ----

        [Test]
        [Category(TestCategory.Contract)]
        public void 混合提交_两片段按权重_就地调权与倍率()
        {
            var (_, backend) = Build();
            backend.RegisterClip("idle", Clip("idle", 1f));
            backend.RegisterClip("run", Clip("run", 1f));

            var blend = new AnimationResolvedBlend(new AnimationId("Test.MoveBlend"), AnimationChannel.Base,
                new[] { "idle", "run" }, new[] { 0.8f, 0.2f }, 0f, 1f);
            Assert.IsTrue(backend.TryPlayBlend(in blend), "直 Clip 混合提交成立");
            Assert.IsTrue(backend.IsChannelActive(AnimationChannel.Base));
            Assert.IsTrue(backend.TryGetChannelDebug(AnimationChannel.Base, out var debug));
            Assert.AreEqual("mixer(2)", debug.Source);

            Assert.IsTrue(backend.TrySetBlendWeights(AnimationChannel.Base, new[] { 0.3f, 0.7f }), "就地调权");
            Assert.IsTrue(backend.TrySetBlendSpeed(AnimationChannel.Base, 1.5f), "就地倍率（步频同步）");

            for (int i = 0; i < 5; i++)
                Assert.AreEqual(AnimationChannelMask.None, backend.Tick(0.3f),
                    "混合节点按循环对待：不产生 Completed（§5）");
        }

        // ---- 片段时长查询（换弹倍率等的消费面） ----

        [Test]
        [Category(TestCategory.Contract)]
        public void 外驱片段时长可查_未知绑定不猜()
        {
            var (_, backend) = Build();
            backend.RegisterClip("reload", Clip("reload", 1.4f));

            Assert.IsTrue(backend.TryGetClipSeconds("reload", out float seconds));
            Assert.AreEqual(1.4f, seconds, 1e-3f, "时长单一来源 = 片段资产");

            Assert.IsFalse(backend.TryGetClipSeconds("ghost", out _), "未知绑定不猜兜底时长");
        }
    }
}
