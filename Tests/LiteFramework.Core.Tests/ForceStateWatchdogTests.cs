using System;
using LiteFramework;
using Xunit;

namespace LiteFramework.Tests
{
    /// <summary>
    /// 强制迁移与驻留看门狗直测（《状态机专项设计》§3.2/§6 验收）：
    /// ForceState 绕准入不绕编程错误检查（重入/未注册/Start 前/OnLeave 窗口照抛）；
    /// 看门狗超限强制迁移与计数、同帧条件边/挂起请求让位、抢占机目标绕准入、层级机最深活动态裁决。
    /// </summary>
    public sealed class ForceStateWatchdogTests
    {
        private enum Tid { A, B, C }

        /// <summary>委托替身：钩子行为注入（含闭包捕获机器做窗口期断言）。</summary>
        private sealed class SpyStage : IStage<Tid, int>
        {
            public Action<IStageHost<Tid, int>> OnInitAction, OnEnterAction, OnUpdateAction, OnLeaveAction;
            public void OnInit(IStageHost<Tid, int> m) => OnInitAction?.Invoke(m);
            public void OnEnter(IStageHost<Tid, int> m, in int req) => OnEnterAction?.Invoke(m);
            public void OnUpdate(IStageHost<Tid, int> m, float elapse) => OnUpdateAction?.Invoke(m);
            public void OnLeave(IStageHost<Tid, int> m) => OnLeaveAction?.Invoke(m);
        }

        private static TableStage<Tid, int> Table(Tid id, int priority = 0, bool interruptible = true,
            int timeoutFrames = 0, Tid timeoutTarget = default)
            => new TableStage<Tid, int>(new StageSpec<Tid, int>
            {
                Id = id,
                Priority = priority,
                CanBeInterrupted = interruptible,
                TimeoutFrames = timeoutFrames,
                TimeoutTarget = timeoutTarget,
            });

        [Fact]
        public void ForceState_基础机等价Request()
        {
            var m = new StageMachine<Tid, int>("F", (Tid.A, new SpyStage()), (Tid.B, new SpyStage()));
            m.Start(Tid.A);

            m.ForceState(Tid.B, 7);
            Assert.Equal(Tid.A, m.Current);                 // 两段式：入队不立即切
            m.Advance();
            Assert.Equal(Tid.B, m.Current);
        }

        [Fact]
        public void ForceState_抢占机绕准入_对照Request被拒()
        {
            // B 霸体 + 优先级 10；C 优先级 0
            var m = new PreemptiveStageMachine<Tid, int>("P",
                (Tid.A, Table(Tid.A)), (Tid.B, Table(Tid.B, priority: 10, interruptible: false)), (Tid.C, Table(Tid.C)));
            m.Start(Tid.B);

            Assert.False(m.Request(Tid.C));                 // 准入拒绝：霸体
            Assert.Equal(RejectReason.InterruptDisallowed, m.LastReject);

            m.ForceState(Tid.C);                            // 绕准入
            Assert.Equal(RejectReason.None, m.LastReject);
            m.Advance();
            Assert.Equal(Tid.C, m.Current);
        }

        [Fact]
        public void ForceState_编程错误照抛_重入未注册Start前OnLeave窗口()
        {
            var m = new StageMachine<Tid, int>("E", (Tid.A, new SpyStage()), (Tid.B, new SpyStage()));
            Assert.Throws<InvalidOperationException>(() => m.ForceState(Tid.B));       // Start 前
            Assert.Throws<InvalidOperationException>(() => m.ForceState((Tid)99));     // 未注册

            m.Start(Tid.A);
            Assert.Throws<InvalidOperationException>(() => m.ForceState(Tid.A));       // 重入

            Exception caught = null;                        // OnLeave 窗口
            StageMachine<Tid, int> m2 = null;
            var leaver = new SpyStage();
            leaver.OnLeaveAction = _ => { try { m2.ForceState(Tid.B); } catch (Exception e) { caught = e; } };
            m2 = new StageMachine<Tid, int>("E2", (Tid.A, leaver), (Tid.B, new SpyStage()));
            m2.Start(Tid.A);
            m2.Request(Tid.B);
            m2.Advance();                                   // 触发 leaver.OnLeave
            Assert.IsType<InvalidOperationException>(caught);
        }

        [Fact]
        public void 看门狗_超限触发强制迁移并计数()
        {
            var m = new StageMachine<Tid, int>("W",
                (Tid.A, new SpyStage()), (Tid.B, Table(Tid.B, timeoutFrames: 3, timeoutTarget: Tid.A)));
            m.Start(Tid.B);

            m.Tick(0.016f);
            m.Tick(0.016f);
            Assert.Equal(Tid.B, m.Current);                 // 驻留 2 帧：未到限
            m.Tick(0.016f);                                 // 第 3 帧 ≥ 3：触发
            Assert.Equal(Tid.A, m.Current);
            Assert.Equal(1, m.TimeoutCount);

            m.Reset();
            Assert.Equal(0, m.TimeoutCount);                // 复位清零
        }

        [Fact]
        public void 看门狗_抢占机上目标低优先级仍强制()
        {
            var m = new PreemptiveStageMachine<Tid, int>("WP",
                (Tid.A, Table(Tid.A)), (Tid.B, Table(Tid.B, priority: 10, interruptible: false, timeoutFrames: 2, timeoutTarget: Tid.A)));
            m.Start(Tid.B);

            Assert.False(m.Request(Tid.A));                 // 常规通道被霸体拒绝
            m.Tick(0.016f);
            m.Tick(0.016f);
            Assert.Equal(Tid.A, m.Current);                 // 看门狗走 ForceState：绕准入
            Assert.Equal(1, m.TimeoutCount);
        }

        [Fact]
        public void 看门狗_同帧条件边命中则让位()
        {
            var t = new TransitionTable<Tid, int>();
            t.AddEdge(Tid.B, Tid.C);                        // 条件边恒真（无守卫）
            var m = new StageMachine<Tid, int>("WC", t,
                (Tid.A, new SpyStage()), (Tid.B, Table(Tid.B, timeoutFrames: 3, timeoutTarget: Tid.A)), (Tid.C, new SpyStage()));
            m.Start(Tid.B);

            m.Tick(0.016f);
            m.Tick(0.016f);
            m.Tick(0.016f);                                 // 第 3 帧：边与看门狗同时到期——边先评估
            Assert.Equal(Tid.C, m.Current);
            Assert.Equal(0, m.TimeoutCount);
        }

        [Fact]
        public void 看门狗_挂起请求优先_看门狗不触发()
        {
            var m = new StageMachine<Tid, int>("WP",
                (Tid.A, new SpyStage()), (Tid.B, Table(Tid.B, timeoutFrames: 2, timeoutTarget: Tid.A)), (Tid.C, new SpyStage()));
            m.Start(Tid.B);

            m.Tick(0.016f);
            Assert.True(m.Request(Tid.C));                  // 第 2 帧前显式请求挂起
            m.Tick(0.016f);                                 // 第 2 帧：有挂起——看门狗不裁决，帧末应用 C
            Assert.Equal(Tid.C, m.Current);
            Assert.Equal(0, m.TimeoutCount);
        }

        [Fact]
        public void 看门狗_未启用零行为()
        {
            var m = new StageMachine<Tid, int>("WN",
                (Tid.A, new SpyStage()), (Tid.B, Table(Tid.B)));    // TimeoutFrames = 0
            m.Start(Tid.B);

            for (int i = 0; i < 10; i++) m.Tick(0.016f);
            Assert.Equal(Tid.B, m.Current);
            Assert.Equal(0, m.TimeoutCount);
        }

        [Fact]
        public void 看门狗_层级机按最深活动态裁决()
        {
            // 树：A（复合）→ B、C；B 声明看门狗（2 帧 → C）
            var m = new HierarchicalStageMachine<Tid, int>("WH",
                new[] { (Tid.A, (IStage<Tid, int>)new SpyStage()),
                        (Tid.B, Table(Tid.B, timeoutFrames: 2, timeoutTarget: Tid.C)),
                        (Tid.C, new SpyStage()) },
                new[] { new CompositeSpec<Tid>(Tid.A, Tid.B, HistoryMode.None, Tid.B, Tid.C) },
                null);
            m.Start(Tid.A);                                 // 展开到 InitialChild=B：活动路径 [A, B]
            Assert.Equal(Tid.B, m.Current);

            m.Tick(0.016f);
            Assert.Equal(Tid.B, m.Current);                 // 1 帧：未到限
            m.Tick(0.016f);                                 // 2 帧 ≥ 2：强制迁 C
            Assert.Equal(Tid.C, m.Current);
            Assert.Equal(1, m.TimeoutCount);
        }
    }
}
