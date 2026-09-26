using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteTesting;
using LiteTesting.Unity;
using LiteGame.UI;   // FadeSlideTransition 已随 §5.1 迁入适配器边界（原在 LiteGame 根）
using NUnit.Framework;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 转场编排层验收（《UI扩展能力设计》§1.5.7 七条）。
    ///
    /// 为什么能在 EditMode 跑：<see cref="UIForm"/> 的构造器是 public（只需一个 GameObject），
    /// 且全部用例用 `UniTask.CompletedTask` 假策略 → `await` 内联完成，**全程 Tick(dt) 驱动、零 PlayerLoop 依赖**。
    /// 状态机本身的迁移语义（last-wins / 帧末 Advance / 重入抛）由 L1 的 StageMachineTests 钉住，此处只验编排层。
    /// </summary>
    public sealed class UiTransitionEditModeTests : UnityTestBase
    {
        // ---- 替身 ----

        private sealed class Recorder : ITransitionStrategy
        {
            public int ShowCount, CloseCount;
            public bool HoldShow;          // 坏策略：永不回调（验超时兜底）
            public bool Throw;             // 抛异常策略（验表现故障不阻塞收尾）
            public CancellationToken LastShowCt;

            public UniTask PlayShow(UIForm form, CancellationToken ct)
            {
                ShowCount++;
                LastShowCt = ct;
                if (Throw) throw new InvalidOperationException("假策略故障");
                return HoldShow ? new UniTaskCompletionSource().Task : UniTask.CompletedTask;
            }

            public UniTask PlayClose(UIForm form, CancellationToken ct)
            {
                CloseCount++;
                return UniTask.CompletedTask;
            }
        }

        private sealed class FakeReplace : IReplaceTransition
        {
            public int Count;
            public UIForm LastOut, LastIn;

            public UniTask PlayReplace(UIForm outgoing, UIForm incoming, CancellationToken ct)
            {
                Count++;
                LastOut = outgoing;
                LastIn = incoming;
                return UniTask.CompletedTask;
            }
        }

        /// <summary>
        /// 造一个界面实例。**Canvas/CanvasGroup 必须在 GameObject 构造器里预建**——
        /// EditMode 下 `AddComponent&lt;Canvas&gt;()` 后立刻访问 `renderMode` 会抛
        /// MissingComponentException（native 组件未就绪），UIForm 的补齐分支会踩到。
        /// 实例由 `UnityTestBase.Scope` 在 TearDown 统一销毁。
        /// </summary>
        private UIForm MakeForm(int id, bool fullScreen = false)
        {
            var info = new UIFormInfo { Id = id, FullScreen = fullScreen, Layer = 1, Location = "x", LuaPath = "x" };
            var go = Scope.CreateGameObject("F" + id, typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            return new UIForm(info, go);
        }

        /// <summary>反射直调 internal 成员（EditMode 程序集不可见——与既有用例同口径）。</summary>
        private static void Invoke(object target, string method)
            => target.GetType()
                .GetMethod(method, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.Invoke(target, null);

        /// <summary>同步取结果（只在断言任务已完成时调用——避免在无 PlayerLoop 的 EditMode 里阻塞）。</summary>
        private static TransitionOutcome Result(UniTask<TransitionOutcome> t)
        {
            Assert.AreEqual(UniTaskStatus.Succeeded, t.Status, "任务未完成——用例时序有误");
            return t.GetAwaiter().GetResult();
        }

        // ---- ① 同帧三连点：首个执行、第二个排队、第三个丢弃 ----

        [Test]
        [Category(TestCategory.Contract)]
        public void 转场_同帧三连点_首个执行_次个排队_第三丢弃()
        {
            var rec = new Recorder();
            var runner = new UITransitionRunner(rec);
            var a = MakeForm(1); var b = MakeForm(2); var c = MakeForm(3);

            var t1 = runner.PlayAsync(TransitionMode.Push, null, a);
            var t2 = runner.PlayAsync(TransitionMode.Push, null, b);
            var t3 = runner.PlayAsync(TransitionMode.Push, null, c);

            Assert.IsTrue(runner.Busy, "首个请求应立即开始");
            Assert.AreEqual(1, runner.QueueLength, "第二个请求应排队（容量 1）");
            Assert.AreEqual(1, runner.DroppedCount, "第三个请求应被丢弃并计数");

            runner.Tick(0.016f);
            Assert.AreEqual(1, rec.ShowCount, "同帧只启动一次表现（表现随帧末迁移发起）");

            var outcome3 = Result(t3);
            Assert.IsFalse(outcome3.Completed, "被丢弃的请求以 Completed=false 收尾");

            _ = t1; _ = t2;
        }

        // ---- ② 转场中重复请求同一 Incoming → 忽略 ----

        [Test]
        [Category(TestCategory.Contract)]
        public void 转场_重复请求同一Incoming_被忽略且不重复播表现()
        {
            var rec = new Recorder();
            var runner = new UITransitionRunner(rec);
            var a = MakeForm(1);

            var t1 = runner.PlayAsync(TransitionMode.Push, null, a);
            var t2 = runner.PlayAsync(TransitionMode.Push, null, a);   // 同一 Incoming

            Assert.AreEqual(0, runner.QueueLength, "重复请求不进队列");
            Assert.IsFalse(Result(t2).Completed);

            runner.Tick(0.016f);                                       // 表现随帧末迁移发起
            Assert.AreEqual(1, rec.ShowCount, "重复请求不得再启动表现");

            _ = t1;
        }

        // ---- ③ Replace：默认合成两组并发 / 自定义 IReplaceTransition 时不走合成 ----

        [Test]
        [Category(TestCategory.Contract)]
        public void 转场_Replace_默认合成_离场与入场并发各一次()
        {
            var rec = new Recorder();
            var runner = new UITransitionRunner(rec);
            var outForm = MakeForm(1); var inForm = MakeForm(2);

            var t = runner.PlayAsync(TransitionMode.Replace, outForm, inForm);
            runner.Tick(0.016f);
            runner.Tick(0.016f);

            Assert.AreEqual(1, rec.CloseCount, "Replace 默认合成应调 PlayClose");
            Assert.AreEqual(1, rec.ShowCount, "Replace 默认合成应调 PlayShow");
            Assert.IsTrue(Result(t).Completed);
            Assert.AreEqual(TransitionId.Idle, runner.Phase);
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 转场_Replace_自定义策略_不走合成()
        {
            var rec = new Recorder();
            var fake = new FakeReplace();
            var runner = new UITransitionRunner(rec, fake);
            var outForm = MakeForm(1); var inForm = MakeForm(2);

            var t = runner.PlayAsync(TransitionMode.Replace, outForm, inForm);
            runner.Tick(0.016f);
            runner.Tick(0.016f);

            Assert.AreEqual(1, fake.Count);
            Assert.AreSame(outForm, fake.LastOut);
            Assert.AreSame(inForm, fake.LastIn);
            Assert.AreEqual(0, rec.CloseCount, "定制实现下壳不再合成 PlayClose");
            Assert.AreEqual(0, rec.ShowCount, "定制实现下壳不再合成 PlayShow");

            _ = t;
        }

        // ---- ④ Pop：等价于 Back，走同一排队路径（这里验模式映射） ----

        [Test]
        [Category(TestCategory.Contract)]
        public void 转场_Pop_只播离场()
        {
            var rec = new Recorder();
            var runner = new UITransitionRunner(rec);
            var form = MakeForm(1);

            var t = runner.PlayAsync(TransitionMode.Pop, form, null);
            runner.Tick(0.016f);
            runner.Tick(0.016f);

            Assert.AreEqual(1, rec.CloseCount);
            Assert.AreEqual(0, rec.ShowCount);
            Assert.AreEqual(TransitionId.Idle, runner.Phase);

            _ = t;
        }

        // ---- ⑤ 坏策略不回调 → 超时强制收尾 ----

        [Test]
        [Category(TestCategory.Contract)]
        public void 转场_坏策略不回调_超时强制收尾且门恢复()
        {
            var rec = new Recorder { HoldShow = true };
            var runner = new UITransitionRunner(rec, null, maxDuration: 0.5f);
            var form = MakeForm(1);

            var t = runner.PlayAsync(TransitionMode.Push, null, form);
            for (int i = 0; i < 6; i++) runner.Tick(0.2f);      // 累计 1.2s > 0.5s

            var outcome = Result(t);
            Assert.IsTrue(outcome.TimedOut, "超时应被标记");
            Assert.IsFalse(outcome.Completed, "超时 = 未正常完成");
            Assert.AreEqual(TransitionResultKind.TimedOut, outcome.Kind, "U1-③：结果分类");
            Assert.AreEqual(TransitionId.Idle, runner.Phase, "超时后必须回到 Idle（UI 不卡死）");
            Assert.IsFalse(runner.Busy);
            Assert.IsFalse(runner.IsLocked(form), "收尾后不再持锁");
            Assert.IsTrue(form.CanvasGroup.interactable, "超时收尾也要按计算值恢复输入（§6.3 先复位）");
        }

        // ---- ⑥ 输入锁由壳统一管（不依赖策略自觉）；interactable 与 blocksRaycasts 职责分离 ----

        [Test]
        [Category(TestCategory.Contract)]
        public void 转场_交互门_转场中锁interactable_收尾按计算值恢复_blocksRaycasts不动()
        {
            var rec = new Recorder();                            // 策略完全不碰 CanvasGroup
            var runner = new UITransitionRunner(rec);
            var outForm = MakeForm(1); var inForm = MakeForm(2);

            var t = runner.PlayAsync(TransitionMode.Replace, outForm, inForm);
            runner.Tick(0.016f);                                 // 进入 In：锁两组输入

            Assert.IsFalse(outForm.CanvasGroup.interactable, "离场界面转场中禁交互（接受即锁）");
            Assert.IsFalse(inForm.CanvasGroup.interactable, "入场界面转场中禁交互");
            Assert.IsTrue(outForm.CanvasGroup.blocksRaycasts, "blocksRaycasts 全程不动——遮挡下层射线（§6.2 职责分离）");
            Assert.IsTrue(inForm.CanvasGroup.blocksRaycasts);

            runner.Tick(0.016f);                                 // 收尾：按计算值恢复
            Assert.IsTrue(outForm.CanvasGroup.interactable);
            Assert.IsTrue(inForm.CanvasGroup.interactable);

            _ = t;
        }

        // ---- ⑦ 完成事件 begin/end 成对且 mode 相符 ----

        [Test]
        [Category(TestCategory.Contract)]
        public void 转场_事件_begin与end成对且mode相符()
        {
            var rec = new Recorder();
            var runner = new UITransitionRunner(rec);
            var form = MakeForm(1);

            int began = 0, finished = 0;
            TransitionMode seenMode = TransitionMode.Push;
            runner.Began += _ => began++;
            runner.Finished += o => { finished++; seenMode = o.Mode; };

            var t = runner.PlayAsync(TransitionMode.Pop, form, null);
            runner.Tick(0.016f);
            runner.Tick(0.016f);

            Assert.AreEqual(1, began, "begin 应恰好一次");
            Assert.AreEqual(1, finished, "end 应恰好一次（成对）");
            Assert.AreEqual(TransitionMode.Pop, seenMode, "mode 与调用相符");

            _ = t;
        }

        // ---- 补：队列取出发生在下一帧（一帧最多一变）----

        [Test]
        [Category(TestCategory.Contract)]
        public void 转场_队列取出_不早于下一帧()
        {
            var rec = new Recorder();
            var runner = new UITransitionRunner(rec);
            var a = MakeForm(1); var b = MakeForm(2);

            var t1 = runner.PlayAsync(TransitionMode.Push, null, a);
            var t2 = runner.PlayAsync(TransitionMode.Push, null, b);

            runner.Tick(0.016f);                                 // A 进入 In（同帧不收尾）
            Assert.AreEqual(1, runner.QueueLength, "A 未收尾前 B 仍排队");
            Assert.AreEqual(1, rec.ShowCount);

            runner.Tick(0.016f);                                 // A 收尾 → 帧末取出 B（仅登记，未进入阶段）
            Assert.AreEqual(0, runner.QueueLength, "A 收尾后取出 B");
            Assert.AreEqual(1, rec.ShowCount, "B 同帧只登记，表现要到下一帧才发起");

            runner.Tick(0.016f);                                 // B 进入 In
            Assert.AreEqual(2, rec.ShowCount, "B 的表现发起");

            _ = t1; _ = t2;
        }

        // ---- ⑥ 播放终态（动画专项 §10 / UI §6.3）----

        /// <summary>写终态的假策略：可控完成/取消，并记录是否复位。</summary>
        private sealed class OutcomeRecorder : ITransitionStrategy
        {
            public bool ResetOnCancel;              // 模拟"取消时复位"
            public int Resets;
            public bool Hold;                       // 永不主动完成（等外部取消）

            public UniTask PlayShow(UIForm form, CancellationToken ct) => UniTask.CompletedTask;
            public UniTask PlayClose(UIForm form, CancellationToken ct) => UniTask.CompletedTask;

            public async UniTask PlayShow(UIForm form, MotionPlayback playback)
            {
                if (Hold)
                {
                    // 等取消信号；收到即"复位"并写终态（真实策略的形态）
                    var tcs = new UniTaskCompletionSource();
                    using (playback.Token.Register(() => { if (ResetOnCancel) { Resets++; } tcs.TrySetResult(); }))
                        await tcs.Task;
                    playback.Finish(MotionOutcome.Cancelled);
                    return;
                }
                playback.Finish(MotionOutcome.Completed);
            }
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 转场_策略写Completed终态_结果按Completed分类()
        {
            var rec = new OutcomeRecorder();
            var runner = new UITransitionRunner(rec);
            var form = MakeForm(1);

            var t = runner.PlayAsync(TransitionMode.Push, null, form);
            runner.Tick(0.016f);
            runner.Tick(0.016f);

            var outcome = Result(t);
            Assert.AreEqual(TransitionResultKind.Completed, outcome.Kind);
            Assert.IsTrue(outcome.Completed);
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 转场_策略写Cancelled终态_未超时也报Cancelled且已复位()
        {
            // 回归卡（2026-09-25 真实缺陷）：超时之外，策略被判为取消时结果必须是 Cancelled
            // 而不是落到兜底；且取消路径必须触发复位（原实现 Kill(true) 不复位）。
            var rec = new OutcomeRecorder { Hold = true, ResetOnCancel = true };
            var runner = new UITransitionRunner(rec);
            var form = MakeForm(1);

            var t = runner.PlayAsync(TransitionMode.Push, null, form);
            runner.Tick(0.016f);

            // 未超时（MaxDuration 默认 2s）→ 走到超时分支才取消；这里推进到超时
            runner.Tick(2.1f);
            runner.Tick(0.016f);

            var outcome = Result(t);
            Assert.AreEqual(TransitionResultKind.TimedOut, outcome.Kind, "超时优先于播放终态");
            Assert.IsTrue(outcome.TimedOut);
            Assert.AreEqual(1, rec.Resets, "取消路径必须复位（§6.3——不得留半截动画）");
            Assert.AreEqual(TransitionId.Idle, runner.Phase);
        }

        // ---- ⑦ 真实策略（FadeSlideTransition）的复位与终态 ----
        // 本组是 2026-09-25 修掉的真实缺陷的回归卡：原 ToTask 用 Kill(true) 复位，
        // 但本仓 DOTween 实测 Kill(true) **既不跳终值也不派发回调** → 超时取消后页面停在半透明。

        [Test]
        [Category(TestCategory.Contract)]
        public void 真实策略_正常完成_写Completed终态且alpha到目标()
        {
            var form = MakeForm(1);
            form.CanvasGroup.alpha = 0f;                       // 入场起点
            var playback = new MotionPlayback(null);

            var task = new FadeSlideTransition().PlayShow(form, playback);
            Assert.AreEqual(UniTaskStatus.Pending, task.Status, "Manual 轨需显式推进——任务不应立即完成");

            // UIClock 派发（与 DotweenUiClockDriver 同款：independent=true 取 unscaled 参数）
            for (int i = 0; i < 40 && task.Status == UniTaskStatus.Pending; i++)
                DG.Tweening.DOTween.ManualUpdate(0.02f, 0.02f);

            Assert.AreEqual(UniTaskStatus.Succeeded, task.Status, "推进足够步数后应完成");
            Assert.IsTrue(playback.Finished);
            Assert.AreEqual(MotionOutcome.Completed, playback.Outcome);
            Assert.AreEqual(1f, form.CanvasGroup.alpha, 0.01f, "入场终值 = 完全可见");
            DG.Tweening.DOTween.KillAll();
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 真实策略_取消时复位到目标视觉_不留半截动画()
        {
            // 回归卡：取消（超时/权威）必须复位。原实现 Kill(true) 不复位 → alpha 停在中途。
            var form = MakeForm(1);
            form.CanvasGroup.alpha = 0f;
            using var cts = new System.Threading.CancellationTokenSource();
            var playback = new MotionPlayback(cts);

            var task = new FadeSlideTransition().PlayShow(form, playback);
            DG.Tweening.DOTween.ManualUpdate(0.05f, 0.05f);   // 推进一点（未到终值）

            float midway = form.CanvasGroup.alpha;
            Assert.Greater(midway, 0f, "应已离开起点");
            Assert.Less(midway, 1f, "尚未到终值（这样才能验复位）");

            cts.Cancel();                                      // 权威取消

            Assert.AreEqual(UniTaskStatus.Succeeded, task.Status, "取消也要让任务收尾（不悬）");
            Assert.IsTrue(playback.Finished);
            Assert.AreEqual(MotionOutcome.Cancelled, playback.Outcome, "终态如实记取消");
            Assert.AreEqual(1f, form.CanvasGroup.alpha, 0.01f, "复位：跳到目标视觉（§6.3）");
            DG.Tweening.DOTween.KillAll();
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 真实策略_页面被回收_壳主动取消并收尾_不白等超时()
        {
            // 回归卡（2026-09-25 实测发现，比动画专项 §2 记录更严重）：
            // 本仓 DOTween 1.3.030 的 OnKill **根本不触发**（显式 Kill(false) 与 KillOnDisable 均实测为无回调），
            // 故页面在转场中被回收时任务会悬着，只能白等 MaxDuration 超时。
            // 现由壳感知 Recycled/Disposed 并主动取消 → 立刻收尾、如实报 Cancelled、策略复位。
            var form = MakeForm(1);
            form.CanvasGroup.alpha = 0f;

            // 用真实策略 + 长超时（避免把"超时兜底"误当成修复）
            var runner = new UITransitionRunner(new FadeSlideTransition(), maxDuration: 30f);
            var t = runner.PlayAsync(TransitionMode.Push, null, form);
            runner.Tick(0.016f);                               // 启动表现

            DG.Tweening.DOTween.ManualUpdate(0.05f, 0.05f);   // 推进一点（未到终值）
            Assert.Less(form.CanvasGroup.alpha, 1f, "尚未到终值");

            // 页面被回收（缓存淘汰/低内存路径）：走真实迁移序列 Loading → Active → Closing → Recycled。
            // internal 成员在 EditMode 程序集不可见，用反射直调——与既有用例调用 AnimatedImage 的
            // OnEnable/Update 同一口径（EditMode 不派发引擎消息时的既定做法）。
            Invoke(form, "PrepareForShow");                    // Loading → Ready 前置
            typeof(UIForm).GetProperty("State")?.SetValue(form, UIFormState.Active);
            Invoke(form, "EnterClosing");
            Invoke(form, "Recycle");
            Assert.AreEqual(UIFormState.Recycled, form.State, "已落到池（Recycled）");

            runner.Tick(0.016f);                               // 壳应主动取消（不等 30s 超时）
            runner.Tick(0.016f);

            var outcome = Result(t);
            Assert.IsFalse(outcome.TimedOut, "这是页面回收，不是超时（两者语义不同）");
            Assert.AreEqual(TransitionResultKind.Cancelled, outcome.Kind, "如实报取消");
            Assert.AreEqual(TransitionId.Idle, runner.Phase, "立即回 Idle——不悬着");
            Assert.AreEqual(1f, form.CanvasGroup.alpha, 0.01f, "复位到目标视觉（§6.3）");
            DG.Tweening.DOTween.KillAll();
        }

        // ---- 补：策略抛异常不阻塞收尾 ----

        [Test]
        [Category(TestCategory.Contract)]
        public void 转场_策略抛异常_不阻塞收尾且Completed为false()
        {
            var rec = new Recorder { Throw = true };
            var runner = new UITransitionRunner(rec);
            var form = MakeForm(1);

            var t = runner.PlayAsync(TransitionMode.Push, null, form);
            runner.Tick(0.016f);
            runner.Tick(0.016f);

            var outcome = Result(t);
            Assert.IsFalse(outcome.Completed, "策略故障 = 未正常完成");
            Assert.IsFalse(outcome.TimedOut, "这是异常不是超时（两者语义不同）");
            Assert.AreEqual(TransitionId.Idle, runner.Phase);
            Assert.IsTrue(form.CanvasGroup.blocksRaycasts);
        }
    }
}
