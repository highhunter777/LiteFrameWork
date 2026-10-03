using System;
using System.Collections.Generic;
using System.Linq;
using LiteFramework;
using Xunit;

namespace LiteFramework.Tests
{
    /// <summary>
    /// 统计与诊断面直测（《状态机专项设计》§4/§6 验收）：审计环（容量/Kind = Manual/Auto/Resume/Forced/Restore、
    /// 旧→新序、last-wins 覆盖不记录）、per-stage StageInfo（进入次数/累计驻留）、计数（拒绝/Auto 边命中/
    /// Resume 成功）、ExportGraph DOT 内容、Snapshot 增量键。
    /// </summary>
    public sealed class FsmAuditStatsTests
    {
        private sealed class Evt { }

        private enum Sid { A, B, C }

        private sealed class SpyStage : IStage<Sid, int>
        {
            public void OnInit(IStageHost<Sid, int> m) { }
            public void OnEnter(IStageHost<Sid, int> m, in int req) { }
            public void OnUpdate(IStageHost<Sid, int> m, float elapse) { }
            public void OnLeave(IStageHost<Sid, int> m) { }
        }

        private static TableStage<Sid, int> Table(Sid id, int priority = 0, bool interruptible = true,
            ResumeMode resume = ResumeMode.Cancel)
            => new TableStage<Sid, int>(new StageSpec<Sid, int> { Id = id, Priority = priority, CanBeInterrupted = interruptible, Resume = resume });

        [Fact]
        public void 审计Kind_手动自动恢复强制齐全()
        {
            var t = new TransitionTable<Sid, int>();
            t.AddEdge<Evt>(Sid.B, Sid.A);                       // 事件边（Auto）
            var m = new PreemptiveStageMachine<Sid, int>("K", t,
                (Sid.A, Table(Sid.A, resume: ResumeMode.Resume)),
                (Sid.B, Table(Sid.B, priority: 10, interruptible: false)),
                (Sid.C, Table(Sid.C)));
            m.Start(Sid.A);

            m.Request(Sid.B);                                   // Manual（10 ≥ 0）
            m.Advance();
            m.ForceState(Sid.C);                                // Forced（绕霸体）
            m.Advance();
            m.Raise(new Evt());                                 // Auto：边命中 B→A（被优先级拒——不入审计）
            m.Tick(0.016f);                                     // 挂起未发生（被拒）→ 无迁移
            Assert.True(m.TryResume());                         // Resume：A 被抢占时入栈（Resume 模式）
            m.Advance();

            var audit = m.GetTransitionAudit();
            Assert.Equal(3, audit.Count);                       // 被拒的事件边不产生迁移记录
            Assert.Equal(TransitionKind.Manual, audit[0].Kind);
            Assert.Equal(Sid.A, audit[0].From);
            Assert.Equal(Sid.B, audit[0].To);
            Assert.Equal(TransitionKind.Forced, audit[1].Kind);
            Assert.Equal(TransitionKind.Resume, audit[2].Kind);
            Assert.Equal(Sid.C, audit[2].From);
            Assert.Equal(Sid.A, audit[2].To);
            Assert.Equal(1, audit[0].Seq);                      // 序号单调
            Assert.Equal(3, audit[2].Seq);
        }

        [Fact]
        public void 审计Kind_自动条件边与快照恢复()
        {
            var t = new TransitionTable<Sid, int>();
            t.AddEdge(Sid.A, Sid.B);                            // 条件边（Auto）
            var m = new StageMachine<Sid, int>("K2", t,
                (Sid.A, new SpyStage()), (Sid.B, new SpyStage()), (Sid.C, new SpyStage()));
            m.Start(Sid.A);

            m.Tick(0.016f);                                     // 条件边命中 → B（Auto）
            var snap = m.Capture();
            m.Request(Sid.C);
            m.Advance();
            m.Restore(snap, FsmRestoreMode.Silent);             // C → B（Restore）

            var audit = m.GetTransitionAudit();
            Assert.Equal(3, audit.Count);
            Assert.Equal(TransitionKind.Auto, audit[0].Kind);
            Assert.Equal(TransitionKind.Manual, audit[1].Kind);
            Assert.Equal(TransitionKind.Restore, audit[2].Kind);
            Assert.Equal(Sid.C, audit[2].From);
            Assert.Equal(Sid.B, audit[2].To);
        }

        [Fact]
        public void 审计环容量32_滚出最老()
        {
            var m = new StageMachine<Sid, int>("R", (Sid.A, new SpyStage()), (Sid.B, new SpyStage()));
            m.Start(Sid.A);
            for (int i = 0; i < 40; i++)                        // A↔B 40 次
            {
                m.Request(i % 2 == 0 ? Sid.B : Sid.A);
                m.Advance();
            }

            var audit = m.GetTransitionAudit();
            Assert.Equal(32, audit.Count);                      // 容量截断
            Assert.Equal(9, (int)audit[0].Seq);                 // 第 9 条起（前 8 条滚出）
            Assert.Equal(40, (int)audit[31].Seq);               // 最新在尾
        }

        [Fact]
        public void per阶段统计_进入次数与累计驻留()
        {
            var m = new StageMachine<Sid, int>("S", (Sid.A, new SpyStage()), (Sid.B, new SpyStage()));
            m.Start(Sid.A);
            m.Tick(0.1f);
            m.Tick(0.1f);
            m.Request(Sid.B);
            m.Advance();                                        // A 驻留 0.2s（完结段）
            m.Tick(0.1f);                                       // B 驻留 0.1s（进行中——尚未计入）

            var infos = m.GetStageInfos().ToDictionary(i => i.Id, i => i);
            Assert.Equal(1, infos[Sid.A].Enters);
            Assert.Equal(1, infos[Sid.B].Enters);
            Assert.Equal(0.2, infos[Sid.A].TotalSeconds, 5);    // 完结段已计入
            Assert.Equal(0.0, infos[Sid.B].TotalSeconds, 5);    // 进行中驻留在 StageTime，迁移离开时才计入

            m.Request(Sid.A);
            m.Advance();
            infos = m.GetStageInfos().ToDictionary(i => i.Id, i => i);
            Assert.Equal(0.1, infos[Sid.B].TotalSeconds, 5);    // B 段完结计入
        }

        [Fact]
        public void 计数_拒绝_Auto边命中_恢复成功()
        {
            var t = new TransitionTable<Sid, int>();
            t.AddEdge<Evt>(Sid.A, Sid.C);
            t.AddEdge(Sid.A, Sid.B);
            var m = new PreemptiveStageMachine<Sid, int>("C", t,
                (Sid.A, Table(Sid.A, priority: 10, resume: ResumeMode.Resume)),   // 可打断但优先级高
                (Sid.B, Table(Sid.B)),
                (Sid.C, Table(Sid.C)));
            m.Start(Sid.A);

            m.Request(Sid.B);                                   // 拒绝：B(0) < A(10)（Priority +1）
            Assert.True(m.Raise(new Evt()));                    // 边命中 = 机器已裁决（返回 true 与准入无关）；C(0) < A(10) → 准入拒（Priority +1）
            Assert.False(m.TryResume());                        // 栈空（ResumeStackEmpty）
            m.ForceState(Sid.C);                                // 强制成功（A 入恢复栈）
            m.Advance();
            Assert.True(m.TryResume());                         // 恢复成功（ResumeSuccess +1）

            var dict = new Dictionary<string, string>();
            m.Snapshot(dict);
            Assert.Equal("1", dict["自动边命中"]);               // 一次事件边命中
            Assert.Equal("2", dict["拒绝-优先级"]);              // Request(B) + Raise 边(C) 各一次
            Assert.Equal("0", dict["拒绝-中断规则"]);
            Assert.Equal("1", dict["恢复成功"]);
        }

        [Fact]
        public void Snapshot增量键_既有键不动()
        {
            var m = new StageMachine<Sid, int>("SS", (Sid.A, new SpyStage()), (Sid.B, new SpyStage()));
            m.Start(Sid.A);
            var dict = new Dictionary<string, string>();
            m.Snapshot(dict);

            Assert.Equal("A", dict["当前阶段"]);                 // 既有键保持
            Assert.Equal("2", dict["阶段数"]);
            Assert.Equal("0", dict["自动边命中"]);               // 增量键就位
            Assert.Equal("0", dict["看门狗触发"]);
            Assert.Equal("0", dict["恢复成功"]);
        }

        [Fact]
        public void ExportGraph_平面机含边与时长链()
        {
            var t = new TransitionTable<Sid, int>();
            t.AddEdge<Evt>(Sid.A, Sid.B);
            t.AddEdge(Sid.B, Sid.C);
            var m = new StageMachine<Sid, int>("G", t,
                (Sid.A, new SpyStage()),
                (Sid.B, new TableStage<Sid, int>(new StageSpec<Sid, int> { Id = Sid.B, DurationFrames = 5, HasNext = true, NextId = Sid.A })),
                (Sid.C, new SpyStage()));
            m.Start(Sid.A);

            var dot = m.ExportGraph();
            Assert.Contains("digraph", dot);
            Assert.Contains("\"A\" -> \"B\" [label=\"evt:Evt\"]", dot);       // 事件边
            Assert.Contains("\"B\" -> \"C\" [label=\"cond\"]", dot);          // 条件边
            Assert.Contains("\"B\" -> \"A\" [label=\"dur5f\", style=dashed]", dot);   // 时长链
        }

        [Fact]
        public void ExportGraph_层级机含复合父子()
        {
            var m = new HierarchicalStageMachine<Sid, int>("GH",
                new[]
                {
                    (Sid.A, (IStage<Sid, int>)new SpyStage()),   // 复合 A
                    (Sid.B, new SpyStage()),
                    (Sid.C, new SpyStage()),
                },
                new[] { new CompositeSpec<Sid>(Sid.A, Sid.B, HistoryMode.None, Sid.B, Sid.C) });
            m.Start(Sid.A);

            var dot = m.ExportGraph();
            Assert.Contains("\"A\" -> \"B\" [style=dotted, label=\"child\"]", dot);
            Assert.Contains("\"A\" -> \"C\" [style=dotted, label=\"child\"]", dot);
        }

        [Fact]
        public void Reset清诊断_层级机审计()
        {
            var m = new HierarchicalStageMachine<Sid, int>("RH",
                new[] { (Sid.A, (IStage<Sid, int>)new SpyStage()), (Sid.B, new SpyStage()) },
                null);
            m.Start(Sid.A);
            m.Request(Sid.B);
            m.Advance();
            Assert.Equal(1, m.GetTransitionAudit().Count);

            m.Reset();
            Assert.Empty(m.GetTransitionAudit());                // 诊断随复位清零
            Assert.All(m.GetStageInfos(), i => Assert.Equal(0, i.Enters));
        }
    }
}
