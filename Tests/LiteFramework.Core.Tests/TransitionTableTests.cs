using System;
using System.Collections.Generic;
using System.Linq;
using LiteFramework;
using Xunit;

namespace LiteFramework.Tests
{
    /// <summary>
    /// TransitionTable 直测（《状态机专项设计》§3.1/§3.5/§6 验收）：
    /// 事件边先于 sink（命中消费）、声明序首个命中、守卫短路（静默回退原语义）、
    /// 条件边不覆盖手动请求、Any 边、Start 封板、构造期 fail-fast（重入/未注册）；
    /// §3.5 深校验（可达性告警/时长死底告警）。
    /// 平面机与层级机两机同判例。
    /// </summary>
    [Collection("CoreStatic")]   // 深校验用例断言 Log.Recent（全局静态）
    public sealed class TransitionTableTests
    {
        private sealed class Go { }
        private sealed class Go2 { }

        /// <summary>哑阶段替身：记录 enter/sink 行为。</summary>
        private sealed class Dummy : IStage<Fsid, int>
        {
            public static Dummy S = new Dummy();
            public int Enters;
            public void OnInit(IStageHost<Fsid, int> m) { }
            public void OnEnter(IStageHost<Fsid, int> m, in int req) => Enters++;
            public void OnUpdate(IStageHost<Fsid, int> m, float elapse) { }
            public void OnLeave(IStageHost<Fsid, int> m) { }
        }

        private enum Fsid { A, B, C, D }

        /// <summary>带 sink 记录的替身（问一次就留痕）。</summary>
        private sealed class SinkStage : IStage<Fsid, int>, IEventSink<Go>
        {
            public int SinkCalls;
            public void OnInit(IStageHost<Fsid, int> m) { }
            public void OnEnter(IStageHost<Fsid, int> m, in int req) { }
            public void OnUpdate(IStageHost<Fsid, int> m, float elapse) { }
            public void OnLeave(IStageHost<Fsid, int> m) { }
            public bool TryHandle(in Go e) { SinkCalls++; return false; }   // 观测不消费——回退链应该走完
        }

        private static StageMachine<Fsid, int> Flat(TransitionTable<Fsid, int> t = null, IStage<Fsid, int> first = null)
            => new StageMachine<Fsid, int>("平面", t, (Fsid.A, first ?? Dummy.S), (Fsid.B, Dummy.S), (Fsid.C, Dummy.S), (Fsid.D, Dummy.S));

        [Fact]
        public void 事件边_命中先于sink_订阅位不被问()
        {
            var t = new TransitionTable<Fsid, int>();
            t.AddEdge<Go>(Fsid.A, Fsid.B);
            var sink = new SinkStage();
            var m = Flat(t, sink);
            m.Start(Fsid.A);

            Assert.True(m.Raise(new Go()));                 // 命中消费——不冒泡不问 sink（两段式：帧末才应用）
            m.Advance();
            Assert.Equal(Fsid.B, m.Current);
            Assert.Equal(0, sink.SinkCalls);               // sink 完全没被问
        }

        [Fact]
        public void 声明序首个命中生效()
        {
            var t = new TransitionTable<Fsid, int>();
            t.AddEdge<Go>(Fsid.A, Fsid.B);
            t.AddEdge<Go>(Fsid.A, Fsid.C);
            var m = Flat(t);
            m.Start(Fsid.A);

            m.Raise(new Go());
            m.Advance();
            Assert.Equal(Fsid.B, m.Current);                // 声明序首条边生效
        }

        [Fact]
        public void 守卫失败_边不触发_回退原语义()
        {
            var t = new TransitionTable<Fsid, int>();
            t.AddEdge<Go>(Fsid.A, Fsid.B, guard: _ => false);
            var sink = new SinkStage();
            var m = Flat(t, sink);
            m.Start(Fsid.A);

            Assert.False(m.Raise(new Go()));                // 未命中 → 原语义（sink 观测未消费）
            Assert.Equal(Fsid.A, m.Current);
            Assert.Equal(1, sink.SinkCalls);               // 回退：问了 sink
        }

        [Fact]
        public void 条件边_命中当帧帧末生效()
        {
            var t = new TransitionTable<Fsid, int>();
            t.AddEdge(Fsid.A, Fsid.B);                     // 条件边（Tick 评估）
            var m = Flat(t);
            m.Start(Fsid.A);

            m.Tick(0.016f);
            Assert.Equal(Fsid.B, m.Current);
        }

        [Fact]
        public void 条件边_不覆盖手动请求()
        {
            var t = new TransitionTable<Fsid, int>();
            t.AddEdge(Fsid.A, Fsid.B);                     // 条件边永远想触发
            var m = Flat(t);
            m.Start(Fsid.A);

            Assert.True(m.Request(Fsid.C));                // 显式请求先入
            m.Tick(0.016f);
            Assert.Equal(Fsid.C, m.Current);               // 自动边让位：显式请求生效
        }

        [Fact]
        public void 任意态是条边_任何态都能触发全面()
        {
            var t = new TransitionTable<Fsid, int>();
            t.AddAnyEdge<Go>(Fsid.D);
            var m = Flat(t);
            m.Start(Fsid.A);

            m.Raise(new Go());
            m.Advance();
            Assert.Equal(Fsid.D, m.Current);

            var m2 = Flat(t);                              // 同表复用：另一台机器同样生效（表是配置）
            m2.Start(Fsid.B);
            m2.Raise(new Go());
            m2.Advance();
            Assert.Equal(Fsid.D, m2.Current);
        }

        [Fact]
        public void 开机封板_Start后加边抛()
        {
            var t = new TransitionTable<Fsid, int>();
            t.AddEdge<Go>(Fsid.A, Fsid.B);
            var m = Flat(t);
            m.Start(Fsid.A);

            Assert.Throws<InvalidOperationException>(() => t.AddEdge<Go>(Fsid.A, Fsid.C));

            var t2 = new TransitionTable<Fsid, int>();
            var m2 = Flat(t2);
            m2.Start(Fsid.A);
            Assert.Throws<InvalidOperationException>(() => t2.AddEdge(Fsid.A, Fsid.B));
        }

        [Fact]
        public void 构造期fail_fast_重入抛_未注册抛()
        {
            Assert.Throws<ArgumentException>(() =>
            {
                var t = new TransitionTable<Fsid, int>();
                t.AddEdge<Go>(Fsid.A, Fsid.A);
            });

            Assert.Throws<InvalidOperationException>(() =>     // 校验由机器构造转调（表自身只记边）
            {
                var t = new TransitionTable<Fsid, int>();
                t.AddEdge<Go>(Fsid.A, (Fsid)99);               // 未注册枚举值
                Flat(t);
            });
        }

        // ---- 层级机同判例 ----

        private static HierarchicalStageMachine<Fsid, int> Hsm(TransitionTable<Fsid, int> t)
            => new HierarchicalStageMachine<Fsid, int>("HSM",
                new[] { (Fsid.A, (IStage<Fsid, int>)Dummy.S), (Fsid.B, Dummy.S), (Fsid.C, Dummy.S), (Fsid.D, Dummy.S) },
                null, t);

        [Fact]
        public void 层级机_事件边先于冒泡_命中消费()
        {
            var t = new TransitionTable<Fsid, int>();
            t.AddEdge<Go>(Fsid.A, Fsid.B);
            var m = Hsm(t);
            m.Start(Fsid.A);

            Assert.True(m.Raise(new Go()));
            m.Advance();
            Assert.Equal(Fsid.B, m.Current);
        }

        [Fact]
        public void 层级机_条件边_不覆盖手动请求()
        {
            var t = new TransitionTable<Fsid, int>();
            t.AddEdge(Fsid.A, Fsid.B);
            var m = Hsm(t);
            m.Start(Fsid.A);

            m.Request(Fsid.C);
            m.Tick(0.016f);
            Assert.Equal(Fsid.C, m.Current);
        }

        [Fact]
        public void 层级机_开机封板()
        {
            var t = new TransitionTable<Fsid, int>();
            var m = Hsm(t);
            m.Start(Fsid.A);
            Assert.Throws<InvalidOperationException>(() => t.AddEdge<Go>(Fsid.A, Fsid.B));
        }

        // ---- §3.5 深校验（Debug 三宏；断言 Log.Recent 按机器名过滤防跨类串读）----

        private static string[] AuditOf(string machineName)
            => Log.Recent.Where(e => e.Message.Contains($"[{machineName}]")).Select(e => e.Message).ToArray();

        [Fact]
        public void 深校验_静态不可达告警_可达不告警()
        {
            Log.ResetForTesting();
            var name = "深校验平面1";
            var t = new TransitionTable<Fsid, int>();
            t.AddEdge<Go>(Fsid.A, Fsid.B);                     // A→B；C/D 无任何自动入口
            var m = new StageMachine<Fsid, int>(name, t,
                (Fsid.A, Dummy.S), (Fsid.B, Dummy.S), (Fsid.C, Dummy.S), (Fsid.D, Dummy.S));
            m.Start(Fsid.A);

            var audit = AuditOf(name);
            Assert.Contains(audit, s => s.Contains("阶段 C 静态不可达"));
            Assert.Contains(audit, s => s.Contains("阶段 D 静态不可达"));
            Assert.DoesNotContain(audit, s => s.Contains("阶段 A"));   // 种子可达
            Assert.DoesNotContain(audit, s => s.Contains("阶段 B"));   // 边可达

            Log.ResetForTesting();
            var name2 = "深校验平面2";
            var t2 = new TransitionTable<Fsid, int>();
            t2.AddEdge<Go>(Fsid.A, Fsid.B);
            t2.AddEdge(Fsid.B, Fsid.C);                        // 条件边补 C 入口
            t2.AddAnyEdge<Go2>(Fsid.D);                        // Any 边补 D 入口
            var m2 = new StageMachine<Fsid, int>(name2, t2,
                (Fsid.A, Dummy.S), (Fsid.B, Dummy.S), (Fsid.C, Dummy.S), (Fsid.D, Dummy.S));
            m2.Start(Fsid.A);
            Assert.Empty(AuditOf(name2));                      // 全员自动可达——零告警
        }

        [Fact]
        public void 深校验_时长死底告警_有出口不告警()
        {
            Log.ResetForTesting();
            var name = "深校验死底";
            var dead = new StageSpec<Fsid, int> { Id = Fsid.B, DurationFrames = 5 };   // 无 NextId/AutoResume
            var alive = new StageSpec<Fsid, int> { Id = Fsid.C, DurationFrames = 5, HasNext = true, NextId = Fsid.A };
            var t = new TransitionTable<Fsid, int>();
            var m = new StageMachine<Fsid, int>(name, t,
                (Fsid.A, Dummy.S), (Fsid.B, new TableStage<Fsid, int>(dead)), (Fsid.C, new TableStage<Fsid, int>(alive)));
            m.Start(Fsid.A);

            var audit = AuditOf(name);
            Assert.Contains(audit, s => s.Contains("阶段 B 配置了时长但无出口"));
            Assert.DoesNotContain(audit, s => s.Contains("阶段 C 配置了时长但无出口"));
        }

        [Fact]
        public void 深校验_层级机跨根_边达子树则父链可达不误报()
        {
            Log.ResetForTesting();
            var name = "深校验层级1";
            // 双根树：R1→R1a、R2→R2a（跨根迁移 = 全退全进）。边 R1a→R2a 使 R2 子树可达 ⇒ 父链（R2）不误报。
            var t = new TransitionTable<Hid, int>();
            t.AddEdge<Go>(Hid.R1a, Hid.R2a);
            var m = new HierarchicalStageMachine<Hid, int>(name,
                new[] { (Hid.R1, (IStage<Hid, int>)HidDummy.S), (Hid.R1a, HidDummy.S),
                        (Hid.R2, HidDummy.S), (Hid.R2a, HidDummy.S) },
                new[] { new CompositeSpec<Hid>(Hid.R1, Hid.R1a, HistoryMode.None, Hid.R1a),
                        new CompositeSpec<Hid>(Hid.R2, Hid.R2a, HistoryMode.None, Hid.R2a) },
                t);
            m.Start(Hid.R1);
            Assert.Empty(AuditOf(name));

            var name2 = "深校验层级2";                          // 反例：无边时 R2 整棵子树是孤岛
            var m2 = new HierarchicalStageMachine<Hid, int>(name2,
                new[] { (Hid.R1, (IStage<Hid, int>)HidDummy.S), (Hid.R1a, HidDummy.S),
                        (Hid.R2, HidDummy.S), (Hid.R2a, HidDummy.S) },
                new[] { new CompositeSpec<Hid>(Hid.R1, Hid.R1a, HistoryMode.None, Hid.R1a),
                        new CompositeSpec<Hid>(Hid.R2, Hid.R2a, HistoryMode.None, Hid.R2a) },
                null);
            m2.Start(Hid.R1);
            var audit = AuditOf(name2);
            Assert.Contains(audit, s => s.Contains("阶段 R2 静态不可达"));
            Assert.Contains(audit, s => s.Contains("阶段 R2a 静态不可达"));
        }

        private enum Hid { R1, R1a, R2, R2a }

        private sealed class HidDummy : IStage<Hid, int>
        {
            public static readonly HidDummy S = new HidDummy();
            public void OnInit(IStageHost<Hid, int> m) { }
            public void OnEnter(IStageHost<Hid, int> m, in int req) { }
            public void OnUpdate(IStageHost<Hid, int> m, float elapse) { }
            public void OnLeave(IStageHost<Hid, int> m) { }
        }
    }
}
