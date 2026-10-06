using System.Collections.Generic;
using LiteSim;
using LiteSim.View;
using LiteTesting;
using LiteTesting.Unity;
using NUnit.Framework;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 命中分发器（<see cref="HitFeedbackDispatcher"/>）的 L2 EditMode 覆盖（《命中反馈与伤害数字专项设计》批A）：
    /// 并列订阅 <see cref="SimView.EventSink"/>（静默门之后）、上下文解析（槽位/位置/数值/本地角色三档）、
    /// 注册序转发、摘订阅语义。事件一律走真链交付（写入缓冲 → <c>SimView.OnFrameEvents</c> → 清空）——
    /// 静默门去重机制本身由 SimView 用例把守，此处只钉分发面。
    /// </summary>
    public sealed class HitFeedbackDispatcherEditModeTests : UnityTestBase
    {
        /// <summary>记录消费者（按抵达序收上下文——转发序与字段断言共用）。</summary>
        private sealed class Recorder : IHitFeedbackConsumer
        {
            public readonly List<HitFeedbackContext> Got = new List<HitFeedbackContext>();
            public void OnHitFeedback(in HitFeedbackContext ctx) => Got.Add(ctx);
        }

        /// <summary>序敏感消费者（把自身序号写进共享序列——两个消费者判转发序）。</summary>
        private sealed class OrderedRecorder : IHitFeedbackConsumer
        {
            private readonly int _ordinal;
            private readonly List<int> _sequence;
            public OrderedRecorder(int ordinal, List<int> sequence) { _ordinal = ordinal; _sequence = sequence; }
            public void OnHitFeedback(in HitFeedbackContext ctx) => _sequence.Add(_ordinal);
        }

        /// <summary>本地实体 (0,1,0) + 逐参数追加 bot；视图对齐本地并落位（槽位解析依赖视图建立）。</summary>
        private (SimWorldState World, SimView View, long Self, long BotA, long BotB) Build(
            params Vector3[] bots)
        {
            var world = new SimWorldState { RngState = 1UL };
            long self = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 1f, 0f) }, out _);
            long botA = 0, botB = 0;
            if (bots.Length > 0)
                botA = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(bots[0].x, bots[0].y, bots[0].z) }, out _);
            if (bots.Length > 1)
                botB = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(bots[1].x, bots[1].y, bots[1].z) }, out _);

            SimView view = new SimView(world, null,
                factory: (loc, parent) => Scope.CreateGameObject(loc),
                recycler: null);
            view.AlignLocal(self);
            view.Tick(1f / 60f);                      // 首帧落位：视图建立（槽位可解析）
            return (world, view, self, botA, botB);
        }

        /// <summary>按真实链路交付一个帧事件（写入缓冲 → 静默门 → EventSink → 分发器）。</summary>
        private static void Deliver(SimView view, SimWorldState world, FrameEventKind kind,
            long entityId, long otherId, int value, SimVector3 pos)
        {
            world.Events.Write(kind, entityId, otherId, value, pos);
            view.OnFrameEvents(world);
            world.Events.Clear();
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 分发_注册序转发_多消费者按注册序收到()
        {
            var (world, view, self, botA, botB) = Build(new Vector3(5f, 1f, 0f), new Vector3(-5f, 1f, 0f));
            var sequence = new List<int>();
            using (var dispatcher = new HitFeedbackDispatcher(view))
            {
                dispatcher.Register(new OrderedRecorder(1, sequence));
                dispatcher.Register(new OrderedRecorder(2, sequence));

                Deliver(view, world, FrameEventKind.Hit, botA, self, 30, new SimVector3(5f, 1f, 0f));

                Assert.AreEqual(2, sequence.Count, "两个消费者各收一次");
                Assert.AreEqual(1, sequence[0], "转发序 = 注册序（先 1 后 2）");
                Assert.AreEqual(2, sequence[1]);
            }
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 分发_本地角色三档_造成承受旁观()
        {
            var (world, view, self, botA, botB) = Build(new Vector3(5f, 1f, 0f), new Vector3(-5f, 1f, 0f));
            var recorder = new Recorder();
            using (var dispatcher = new HitFeedbackDispatcher(view))
            {
                dispatcher.Register(recorder);

                Deliver(view, world, FrameEventKind.Hit, botA, self, 30, default);      // 本地打 botA → 造成
                Deliver(view, world, FrameEventKind.Hit, self, botB, 25, default);      // botB 打本地 → 承受
                Deliver(view, world, FrameEventKind.Hit, botA, botB, 20, default);     // bot 互殴 → 旁观

                Assert.AreEqual(3, recorder.Got.Count);
                Assert.AreEqual(HitLocalRole.Caused, recorder.Got[0].LocalRole, "本地是射手 → 造成");
                Assert.AreEqual(HitLocalRole.Received, recorder.Got[1].LocalRole, "本地是目标 → 承受");
                Assert.AreEqual(HitLocalRole.Bystander, recorder.Got[2].LocalRole, "bot 互殴 → 旁观");
            }
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 分发_自伤优先承受档()
        {
            var (world, view, self, _, _) = Build();
            var recorder = new Recorder();
            using (var dispatcher = new HitFeedbackDispatcher(view))
            {
                dispatcher.Register(recorder);

                Deliver(view, world, FrameEventKind.Hit, self, self, 10, default);     // 主体与对象同为本地

                Assert.AreEqual(HitLocalRole.Received, recorder.Got[0].LocalRole, "自伤（主体=本地）优先记承受");
            }
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 分发_Hit上下文字段_值位置槽位对象槽位()
        {
            var (world, view, self, botA, _) = Build(new Vector3(5f, 1f, 0f));
            var recorder = new Recorder();
            using (var dispatcher = new HitFeedbackDispatcher(view))
            {
                dispatcher.Register(recorder);

                Deliver(view, world, FrameEventKind.Hit, botA, self, 30, new SimVector3(5f, 0f, -0.2f));

                HitFeedbackContext ctx = recorder.Got[0];
                Assert.AreEqual(FrameEventKind.Hit, ctx.Kind);
                Assert.AreEqual(botA, ctx.EntityId, "主体 = 命中目标");
                Assert.AreEqual(self, ctx.OtherId, "对象 = 射手");
                Assert.AreEqual(30, ctx.Value, "数值 = 伤害量");
                Assert.AreEqual(new Vector3(5f, 0f, -0.2f), ctx.WorldPos, "位置 Sim 直映世界");
                Assert.AreEqual(1, ctx.Slot, "botA 槽位（第二个生成）");
                Assert.AreEqual(0, ctx.OtherSlot, "本地槽位（首个生成）");
            }
        }

        [Test]
        [Category(TestCategory.Unit)]
        public void 分发_Fire无对象_对象槽位负一_本地开火为造成()
        {
            var (world, view, self, botA, _) = Build(new Vector3(5f, 1f, 0f));
            var recorder = new Recorder();
            using (var dispatcher = new HitFeedbackDispatcher(view))
            {
                dispatcher.Register(recorder);

                Deliver(view, world, FrameEventKind.Fire, self, 0L, 0, new SimVector3(0.35f, 1f, 0f));
                Deliver(view, world, FrameEventKind.Fire, botA, 0L, 0, new SimVector3(5f, 1f, 0f));

                Assert.AreEqual(HitLocalRole.Caused, recorder.Got[0].LocalRole, "本地开火 → 造成");
                Assert.AreEqual(-1, recorder.Got[0].OtherSlot, "Fire 无对象 → 对象槽位 -1");
                Assert.AreEqual(HitLocalRole.Bystander, recorder.Got[1].LocalRole, "bot 开火 → 旁观");
            }
        }

        [Test]
        [Category(TestCategory.Unit)]
        public void 分发_未知主体槽位负一_如实解析不造事实()
        {
            var (world, view, self, _, _) = Build();
            var recorder = new Recorder();
            using (var dispatcher = new HitFeedbackDispatcher(view))
            {
                dispatcher.Register(recorder);

                Deliver(view, world, FrameEventKind.Hit, 999L, self, 30, default);     // 不存在的实体 Id

                Assert.AreEqual(-1, recorder.Got[0].Slot, "主体不存在 → 槽位 -1（诚实退化，不造事实）");
                Assert.AreEqual(0, recorder.Got[0].OtherSlot, "射手（本地）槽位照常解析");
            }
        }

        [Test]
        [Category(TestCategory.Unit)]
        public void 分发_摘订阅_Dispose后事件不再到达()
        {
            var (world, view, self, botA, _) = Build(new Vector3(5f, 1f, 0f));
            var recorder = new Recorder();
            var dispatcher = new HitFeedbackDispatcher(view);
            dispatcher.Register(recorder);

            dispatcher.Dispose();
            Deliver(view, world, FrameEventKind.Hit, botA, self, 30, default);

            Assert.AreEqual(0, recorder.Got.Count, "Dispose 摘订阅后不再分发");
            Assert.AreEqual(0, view.EventSink?.GetInvocationList().Length ?? 0, "EventSink 上无残留订阅");
        }

        [Test]
        [Category(TestCategory.Unit)]
        public void 分发_Unregister单个摘除_其余照常消费()
        {
            var (world, view, self, botA, _) = Build(new Vector3(5f, 1f, 0f));
            var keep = new Recorder();
            var drop = new Recorder();
            using (var dispatcher = new HitFeedbackDispatcher(view))
            {
                dispatcher.Register(keep);
                dispatcher.Register(drop);
                Assert.IsTrue(dispatcher.Unregister(drop), "摘除已注册消费者成功");

                Deliver(view, world, FrameEventKind.Hit, botA, self, 30, default);

                Assert.AreEqual(1, keep.Got.Count, "在册消费者照常收到");
                Assert.AreEqual(0, drop.Got.Count, "已摘除消费者不再收到");
            }
        }
    }
}
