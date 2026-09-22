using System;
using Cysharp.Threading.Tasks;
using LiteTesting;
using LiteTesting.Unity;
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

            public UniTask PlayShow(UIForm form)
            {
                ShowCount++;
                if (Throw) throw new InvalidOperationException("假策略故障");
                return HoldShow ? new UniTaskCompletionSource().Task : UniTask.CompletedTask;
            }

            public UniTask PlayClose(UIForm form)
            {
                CloseCount++;
                return UniTask.CompletedTask;
            }
        }

        private sealed class FakeReplace : IReplaceTransition
        {
            public int Count;
            public UIForm LastOut, LastIn;

            public UniTask PlayReplace(UIForm outgoing, UIForm incoming)
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
            Assert.AreEqual(TransitionId.Idle, runner.Phase, "超时后必须回到 Idle（UI 不卡死）");
            Assert.IsFalse(runner.Busy);
            Assert.IsTrue(form.CanvasGroup.blocksRaycasts, "超时收尾也要恢复交互门");
        }

        // ---- ⑥ 交互门由壳统一管（不依赖策略自觉）----

        [Test]
        [Category(TestCategory.Contract)]
        public void 转场_交互门_转场中关闭_收尾后恢复()
        {
            var rec = new Recorder();                            // 策略完全不碰 CanvasGroup
            var runner = new UITransitionRunner(rec);
            var outForm = MakeForm(1); var inForm = MakeForm(2);

            var t = runner.PlayAsync(TransitionMode.Replace, outForm, inForm);
            runner.Tick(0.016f);                                 // 进入 In：关两组门

            Assert.IsFalse(outForm.CanvasGroup.blocksRaycasts, "离场界面转场中禁交互");
            Assert.IsFalse(inForm.CanvasGroup.blocksRaycasts, "入场界面转场中禁交互");

            runner.Tick(0.016f);                                 // 收尾：恢复
            Assert.IsTrue(outForm.CanvasGroup.blocksRaycasts);
            Assert.IsTrue(inForm.CanvasGroup.blocksRaycasts);

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
