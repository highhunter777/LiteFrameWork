using System;
using System.Collections.Generic;
using LiteFramework;
using Xunit;

namespace LiteFramework.Tests
{
    /// <summary>
    /// 快照与恢复直测（《状态机专项设计》§3.4/§6 验收）：
    /// Hooks 回调序（层级机深→浅退/浅→深进、平面机 Leave→Enter）、Silent 零回调与浮点按位、
    /// 两模式清挂起、恢复栈随快照往返、层级机历史与精确路径恢复（不重展开）、非法时机抛、
    /// Silent 确定性重放（回滚后重放与连续运行逐位一致）。
    /// </summary>
    public sealed class FsmCaptureRestoreTests
    {
        private enum Sid { A, B, C }

        /// <summary>回调记录替身：Enter 记 "X+"，Leave 记 "X-"。</summary>
        private sealed class LogStage : IStage<Sid, int>
        {
            private readonly List<string> _log;
            private readonly string _name;
            public LogStage(List<string> log, string name) { _log = log; _name = name; }
            public void OnInit(IStageHost<Sid, int> m) { }
            public void OnEnter(IStageHost<Sid, int> m, in int req) => _log.Add(_name + "+");
            public void OnUpdate(IStageHost<Sid, int> m, float elapse) { }
            public void OnLeave(IStageHost<Sid, int> m) => _log.Add(_name + "-");
        }

        private static StageMachine<Sid, int> Flat(List<string> log)
            => new StageMachine<Sid, int>("F",
                (Sid.A, new LogStage(log, "A")), (Sid.B, new LogStage(log, "B")), (Sid.C, new LogStage(log, "C")));

        [Fact]
        public void 平面机_Hooks回调序_旧Leave新Enter()
        {
            var log = new List<string>();
            var m = Flat(log);
            m.Start(Sid.A);
            m.Request(Sid.B);
            m.Advance();                                   // [B+]
            var snap = m.Capture();

            m.Request(Sid.C);
            m.Advance();                                   // [B-, C+]
            log.Clear();
            m.Restore(snap, FsmRestoreMode.Hooks);

            Assert.Equal(Sid.B, m.Current);
            Assert.Equal(new[] { "C-", "B+" }, log);       // 旧 Leave → 新 Enter(default)
        }

        [Fact]
        public void 平面机_Silent零回调_浮点按位置回()
        {
            var log = new List<string>();
            var m = Flat(log);
            m.Start(Sid.A);
            for (int i = 0; i < 3; i++) m.Tick(0.033f);
            var snap = m.Capture();
            var frozenTime = m.StageTime;
            var frozenFrames = m.StageFrames;

            m.Request(Sid.C);
            m.Tick(0.033f);                                // 迁移 + 驻留推进
            m.Tick(0.033f);
            log.Clear();
            m.Restore(snap, FsmRestoreMode.Silent);

            Assert.Equal(Sid.A, m.Current);                // 纯置换回 A
            Assert.Empty(log);                             // 零回调
            Assert.Equal(frozenFrames, m.StageFrames);
            Assert.Equal(frozenTime, m.StageTime);         // 浮点按位（== 即逐位）
        }

        [Fact]
        public void 平面机_两模式都清挂起()
        {
            var log = new List<string>();
            var m = Flat(log);
            m.Start(Sid.A);
            m.Request(Sid.B);
            m.Advance();
            var snap = m.Capture();

            Assert.True(m.Request(Sid.C));                 // 挂起
            m.Restore(snap, FsmRestoreMode.Silent);
            Assert.False(m.HasPending);                    // 结构性裁决：挂起让位
            Assert.Equal(Sid.B, m.Current);
            m.Advance();                                   // 无挂起 → no-op
            Assert.Equal(Sid.B, m.Current);
        }

        [Fact]
        public void 抢占机_恢复栈随快照往返()
        {
            TableStage<Sid, int> Atk() => new TableStage<Sid, int>(new StageSpec<Sid, int>
            { Id = Sid.A, Priority = 10, Resume = ResumeMode.Resume });
            TableStage<Sid, int> Hurt() => new TableStage<Sid, int>(new StageSpec<Sid, int>
            { Id = Sid.C, Priority = 20 });

            var m = new PreemptiveStageMachine<Sid, int>("P",
                (Sid.A, Atk()), (Sid.B, new LogStage(new List<string>(), "B")), (Sid.C, Hurt()));
            m.Start(Sid.A);
            m.Request(Sid.C);
            m.Advance();                                   // A 被抢占入栈
            Assert.Equal(1, m.ResumeDepth);
            var snap = m.Capture();

            Assert.True(m.TryResume());                    // 恢复（栈清空）
            m.Advance();
            Assert.Equal(0, m.ResumeDepth);

            m.Restore(snap, FsmRestoreMode.Silent);
            Assert.Equal(1, m.ResumeDepth);                // 栈随快照回来
            Assert.Equal(Sid.C, m.Current);                // 结构态同快照
        }

        private enum Hid { Root, A, A1, A2, B }

        private sealed class HLog : IStage<Hid, int>
        {
            private readonly List<string> _log;
            private readonly string _name;
            public HLog(List<string> log, string name) { _log = log; _name = name; }
            public void OnInit(IStageHost<Hid, int> m) { }
            public void OnEnter(IStageHost<Hid, int> m, in int req) => _log.Add(_name + "+");
            public void OnUpdate(IStageHost<Hid, int> m, float elapse) { }
            public void OnLeave(IStageHost<Hid, int> m) => _log.Add(_name + "-");
        }

        /// <summary>树：Root(复合)→A、B；A(复合,Shallow)→A1、A2。</summary>
        private static HierarchicalStageMachine<Hid, int> Tree(List<string> log)
            => new HierarchicalStageMachine<Hid, int>("H",
                new[]
                {
                    (Hid.Root, (IStage<Hid, int>)new HLog(log, "Root")),
                    (Hid.A, new HLog(log, "A")),
                    (Hid.A1, new HLog(log, "A1")),
                    (Hid.A2, new HLog(log, "A2")),
                    (Hid.B, new HLog(log, "B")),
                },
                new[]
                {
                    new CompositeSpec<Hid>(Hid.Root, Hid.A, HistoryMode.Shallow, Hid.A, Hid.B),
                    new CompositeSpec<Hid>(Hid.A, Hid.A1, HistoryMode.Shallow, Hid.A1, Hid.A2),
                });

        [Fact]
        public void 层级机_Hooks回调序_深到浅退浅到深进()
        {
            var log = new List<string>();
            var m = Tree(log);
            m.Start(Hid.Root);                             // [Root, A, A1]
            m.Request(Hid.B);
            m.Advance();                                   // → [Root, B]（A 记历史 A1）
            var snap = m.Capture();

            m.Request(Hid.A);                              // → [Root, A, A1]（历史展开）
            m.Advance();
            m.Request(Hid.A2);                             // → [Root, A, A2]
            m.Advance();
            log.Clear();
            m.Restore(snap, FsmRestoreMode.Hooks);         // → [Root, B]

            Assert.Equal(Hid.B, m.Current);
            Assert.Equal(new[] { "A2-", "A-", "B+" }, log);    // 分歧尾深→浅退，目标尾浅→深进
        }

        [Fact]
        public void 层级机_精确路径恢复_不重展开()
        {
            var log = new List<string>();
            var m = Tree(log);
            m.Start(Hid.Root);                             // [Root, A, A1]
            m.Request(Hid.A2);                             // 显式到 A2：[Root, A, A2]
            m.Advance();
            var snap = m.Capture();
            Assert.Equal(3, m.ActivePath.Count);
            Assert.Equal(Hid.A2, m.ActivePath[2]);

            m.Request(Hid.B);
            m.Advance();
            m.Restore(snap, FsmRestoreMode.Hooks);

            Assert.Equal(Hid.A2, m.Current);               // 快照原路径（不是初始/历史展开的 A1）
            Assert.Equal(3, m.ActivePath.Count);
        }

        [Fact]
        public void 层级机_历史随快照置回()
        {
            var log = new List<string>();
            var m = Tree(log);
            m.Start(Hid.Root);                             // [Root, A, A1]
            m.Request(Hid.A2);
            m.Advance();                                   // A2 活动（A 历史在子态切换时刷为 A2）
            m.Request(Hid.B);
            m.Advance();                                   // → [Root, B]；A 历史 = [A2]
            var snap = m.Capture();

            m.Request(Hid.A);                              // 消耗历史：[Root, A, A2]
            m.Advance();
            m.Request(Hid.B);
            m.Advance();                                   // A 历史仍 = [A2]
            m.Restore(snap, FsmRestoreMode.Silent);        // 回到快照（历史 = [A2]）

            m.Request(Hid.A);                              // 快照态再展开
            m.Advance();
            Assert.Equal(Hid.A2, m.Current);               // 历史从快照来：展开到 A2
        }

        [Fact]
        public void 层级机_Silent零回调()
        {
            var log = new List<string>();
            var m = Tree(log);
            m.Start(Hid.Root);
            m.Request(Hid.B);
            m.Advance();
            var snap = m.Capture();

            m.Request(Hid.A2);
            m.Advance();
            log.Clear();
            m.Restore(snap, FsmRestoreMode.Silent);

            Assert.Empty(log);                             // 零回调
            Assert.Equal(Hid.B, m.Current);
        }

        [Fact]
        public void 非法时机抛_Start前回调内OnLeave窗口()
        {
            var log = new List<string>();
            var m = Flat(log);
            Assert.Throws<InvalidOperationException>(() => m.Capture());        // Start 前
            m.Start(Sid.A);
            var snap = m.Capture();

            // 阶段回调内（OnUpdate）
            StageMachine<Sid, int> m2 = null;
            var updater = new List<string>();
            var probe = new ProbeStage { OnUpdateAction = () => { try { m2.Restore(snap); } catch (Exception e) { updater.Add(e.GetType().Name); } } };
            m2 = new StageMachine<Sid, int>("F2", (Sid.A, probe), (Sid.B, new LogStage(updater, "B")), (Sid.C, new LogStage(updater, "C")));
            m2.Start(Sid.A);
            m2.Tick(0.016f);
            Assert.Contains("InvalidOperationException", updater);

            // OnLeave 窗口
            Exception caught = null;
            StageMachine<Sid, int> m3 = null;
            var leaver = new ProbeStage { OnLeaveAction = () => { try { m3.Restore(snap); } catch (Exception e) { caught = e; } } };
            m3 = new StageMachine<Sid, int>("F3", (Sid.A, leaver), (Sid.B, new LogStage(new List<string>(), "B")), (Sid.C, new LogStage(new List<string>(), "C")));
            m3.Start(Sid.A);
            m3.Request(Sid.B);
            m3.Advance();
            Assert.IsType<InvalidOperationException>(caught);
        }

        private sealed class ProbeStage : IStage<Sid, int>
        {
            public Action OnUpdateAction, OnLeaveAction;
            public void OnInit(IStageHost<Sid, int> m) { }
            public void OnEnter(IStageHost<Sid, int> m, in int req) { }
            public void OnUpdate(IStageHost<Sid, int> m, float elapse) => OnUpdateAction?.Invoke();
            public void OnLeave(IStageHost<Sid, int> m) => OnLeaveAction?.Invoke();
        }

        [Fact]
        public void 结构已变_快照id未注册抛()
        {
            var full = Flat(new List<string>());
            full.Start(Sid.A);
            full.Request(Sid.C);
            full.Advance();
            var snap = full.Capture();                     // Current = C

            var smaller = new StageMachine<Sid, int>("S",   // 只注册 A/B
                (Sid.A, new LogStage(new List<string>(), "A")), (Sid.B, new LogStage(new List<string>(), "B")));
            smaller.Start(Sid.A);
            Assert.Throws<InvalidOperationException>(() => smaller.Restore(snap, FsmRestoreMode.Silent));
        }

        [Fact]
        public void Silent确定性重放_回滚后重放与连续运行逐位一致()
        {
            // 表驱动往返：A(时长3→B)、B(时长5→A)——重放期间输入固定步长
            StageMachine<Sid, int> Build() => new StageMachine<Sid, int>("R",
                (Sid.A, new TableStage<Sid, int>(new StageSpec<Sid, int> { Id = Sid.A, DurationFrames = 3, HasNext = true, NextId = Sid.B })),
                (Sid.B, new TableStage<Sid, int>(new StageSpec<Sid, int> { Id = Sid.B, DurationFrames = 5, HasNext = true, NextId = Sid.A })));

            var straight = Build();
            straight.Start(Sid.A);
            var rolled = Build();
            rolled.Start(Sid.A);

            for (int i = 0; i < 7; i++) { straight.Tick(0.033f); rolled.Tick(0.033f); }

            var snap = rolled.Capture();                   // 第 7 帧（B 驻留 4 帧）
            for (int i = 0; i < 4; i++) rolled.Tick(0.033f);   // 漂移 4 帧（含 B→A 迁移）
            rolled.Restore(snap, FsmRestoreMode.Silent);   // 回滚到第 7 帧

            for (int i = 0; i < 4; i++) { straight.Tick(0.033f); rolled.Tick(0.033f); }   // 同输入重放

            Assert.Equal(straight.Current, rolled.Current);
            Assert.Equal(straight.StageTime, rolled.StageTime);       // 浮点按位
            Assert.Equal(straight.StageFrames, rolled.StageFrames);
            Assert.Equal(straight.TransitionCount, rolled.TransitionCount);
        }
    }
}
