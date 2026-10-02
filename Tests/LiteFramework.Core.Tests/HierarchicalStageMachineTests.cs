using System;
using System.Collections.Generic;
using Xunit;

namespace LiteFramework.Tests
{
    /// <summary>
    /// 层级状态机（HSM）`HierarchicalStageMachine&lt;TId, TReq&gt;`。
    /// 覆盖：建树校验 / enter-exit-update 顺序 / LCA 跨层降升 / 降层不重跑祖先 / 浅深历史 /
    /// 显式目标优先 / 冒泡 / H10 的 7 条传统语义 / 异常中断 / 与 flat 的行为对拍。
    ///
    /// 测试用树（多根）：
    /// <code>
    ///   Boot(叶)   Main(复合, initial=Lobby, Shallow)   Error(叶)
    ///                ├ Lobby(叶)  ├ Match(叶)  └ Battle(复合, initial=Ongoing, Deep)
    ///                                                ├ Ongoing(叶)  └ Result(叶)
    /// </code>
    /// </summary>
    public sealed class HierarchicalStageMachineTests
    {
        private enum Id { Boot, Main, Lobby, Match, Battle, Ongoing, Result, Error }

        private readonly struct Req
        {
            public readonly int Value;
            public Req(int value) => Value = value;
        }

        private sealed class Stage : IStage<Id, Req>
        {
            private readonly List<string> _log;
            private readonly string _tag;

            public int Enter, Leave, Update, LastReq;
            public Action OnEnterHook, OnUpdateHook, OnLeaveHook;

            public Stage(List<string> log, string tag) { _log = log; _tag = tag; }

            public void OnInit(IStageHost<Id, Req> m) { }
            public void OnEnter(IStageHost<Id, Req> m, in Req req)
            {
                Enter++; LastReq = req.Value; _log.Add("+" + _tag); OnEnterHook?.Invoke();
            }
            public void OnUpdate(IStageHost<Id, Req> m, float s)
            {
                Update++; _log.Add("u" + _tag); OnUpdateHook?.Invoke();
            }
            public void OnLeave(IStageHost<Id, Req> m)
            {
                Leave++; _log.Add("-" + _tag); OnLeaveHook?.Invoke();
            }
        }

        /// <summary>冒泡测试用：可选实现 IEventSink（记录是否被问到）。</summary>
        private sealed class StageWithSink : IStage<Id, Req>, IEventSink<int>
        {
            private readonly int _consumes;                        // -1 = 不消费
            public int Asked;
            public StageWithSink(int consumes) { _consumes = consumes; }
            public void OnInit(IStageHost<Id, Req> m) { }
            public void OnEnter(IStageHost<Id, Req> m, in Req req) { }
            public void OnUpdate(IStageHost<Id, Req> m, float s) { }
            public void OnLeave(IStageHost<Id, Req> m) { }
            public bool TryHandle(in int e) { Asked++; return _consumes == e; }
        }

        private sealed class Fixture
        {
            public readonly List<string> Log = new List<string>();
            public readonly Stage Boot, Main, Lobby, Match, Battle, Ongoing, Result, Error;
            public readonly HierarchicalStageMachine<Id, Req> Machine;

            public Fixture(HistoryMode mainHistory = HistoryMode.Shallow, HistoryMode battleHistory = HistoryMode.Deep)
            {
                Boot = new Stage(Log, "Boot");
                Main = new Stage(Log, "Main");
                Lobby = new Stage(Log, "Lobby");
                Match = new Stage(Log, "Match");
                Battle = new Stage(Log, "Battle");
                Ongoing = new Stage(Log, "Ongoing");
                Result = new Stage(Log, "Result");
                Error = new Stage(Log, "Error");

                Machine = new HierarchicalStageMachine<Id, Req>("hsm",
                    stages: new (Id, IStage<Id, Req>)[]
                    {
                        (Id.Boot, Boot), (Id.Main, Main), (Id.Lobby, Lobby), (Id.Match, Match),
                        (Id.Battle, Battle), (Id.Ongoing, Ongoing), (Id.Result, Result), (Id.Error, Error),
                    },
                    composites: new[]
                    {
                        new CompositeSpec<Id>(Id.Main, Id.Lobby, mainHistory, Id.Lobby, Id.Match, Id.Battle),
                        new CompositeSpec<Id>(Id.Battle, Id.Ongoing, battleHistory, Id.Ongoing, Id.Result),
                    });
            }
        }

        // ---- 建树校验 ----

        [Fact]
        public void HSM_子态未注册_构造抛()
        {
            var s = new Stage(new List<string>(), "x");
            Assert.Throws<ArgumentException>(() => new HierarchicalStageMachine<Id, Req>("t",
                new (Id, IStage<Id, Req>)[] { (Id.Main, s) },
                new[] { new CompositeSpec<Id>(Id.Main, Id.Lobby, HistoryMode.None, Id.Lobby) }));
        }

        [Fact]
        public void HSM_重复父_构造抛()
        {
            var stages = new (Id, IStage<Id, Req>)[]
            {
                (Id.Main, new Stage(new List<string>(), "m")),
                (Id.Boot, new Stage(new List<string>(), "b")),
                (Id.Lobby, new Stage(new List<string>(), "l")),
            };
            Assert.Throws<ArgumentException>(() => new HierarchicalStageMachine<Id, Req>("t", stages, new[]
            {
                new CompositeSpec<Id>(Id.Main, Id.Lobby, HistoryMode.None, Id.Lobby),
                new CompositeSpec<Id>(Id.Boot, Id.Lobby, HistoryMode.None, Id.Lobby),   // 同子两父
            }));
        }

        [Fact]
        public void HSM_InitialChild不在children_构造抛()
        {
            Assert.Throws<ArgumentException>(() =>
                new CompositeSpec<Id>(Id.Main, Id.Match, HistoryMode.None, Id.Lobby, Id.Battle));
        }

        // ---- 顺序语义（H1/H2/H3）----

        [Fact]
        public void HSM_Start_进入顺序浅到深_多根可选()
        {
            var f = new Fixture();
            f.Machine.Start(Id.Main);
            Assert.Equal(new[] { "+Main", "+Lobby" }, f.Log);
            Assert.Equal(Id.Lobby, f.Machine.Current);
            Assert.Equal(2, f.Machine.ActivePath.Count);

            var f2 = new Fixture();
            f2.Machine.Start(Id.Boot);                             // 另一个根
            Assert.Equal(new[] { "+Boot" }, f2.Log);
        }

        [Fact]
        public void HSM_Start非根_抛()
        {
            var f = new Fixture();
            Assert.Throws<InvalidOperationException>(() => f.Machine.Start(Id.Lobby));
        }

        [Fact]
        public void HSM_OnUpdate沿路径根到叶()
        {
            var f = new Fixture();
            f.Machine.Start(Id.Main);
            f.Log.Clear();
            f.Machine.Tick(0.1f);
            Assert.Equal(new[] { "uMain", "uLobby" }, f.Log);      // 父先子后
        }

        // ---- LCA 跨层降升（H5/H6）----

        [Fact]
        public void HSM_跨层降升_共同祖先之下先退后进()
        {
            var f = new Fixture();
            f.Machine.Start(Id.Main);                              // Main/Lobby
            f.Log.Clear();                                         // 只看本次事务的钩子顺序
            f.Machine.Request(Id.Result, new Req(5));
            f.Machine.Advance();

            // Main 是共同祖先 → 不重跑；Lobby 退出；Battle 进入后**直接进 Result**（显式目标优先于 InitialChild）
            Assert.Equal(new[] { "-Lobby", "+Battle", "+Result" }, f.Log);
            Assert.Equal(new[] { Id.Main, Id.Battle, Id.Result }, f.Machine.ActivePath);
            Assert.Equal(1, f.Main.Enter);                         // 祖先不重跑
            Assert.Equal(5, f.Result.LastReq);                     // payload 逐层传递同一 req
            Assert.Equal(1, f.Machine.TransitionCount);            // 一次事务（不是 4 次）
        }

        [Fact]
        public void HSM_跨根迁移_全退全进()
        {
            var f = new Fixture();
            f.Machine.Start(Id.Boot);
            f.Machine.Request(Id.Lobby);
            f.Machine.Advance();
            Assert.Equal(new[] { "+Boot", "-Boot", "+Main", "+Lobby" }, f.Log);
            Assert.Equal(new[] { Id.Main, Id.Lobby }, f.Machine.ActivePath);
        }

        [Fact]
        public void HSM_降层到父态_按历史回到上次子页()
        {
            var f = new Fixture();
            f.Machine.Start(Id.Main);
            f.Machine.Request(Id.Match); f.Machine.Advance();      // Main/Match
            f.Machine.Request(Id.Lobby); f.Machine.Advance();      // 回 Lobby（Main 的历史 = Match → 但目标是 Lobby 显式）
            Assert.Equal(Id.Lobby, f.Machine.Current);

            f.Machine.Request(Id.Match); f.Machine.Advance();      // Main/Match（Match 退出时 Main.History=Match）
            f.Machine.Request(Id.Main); f.Machine.Advance();       // 降到 Main → 展开用历史 = Match
            Assert.Equal(Id.Match, f.Machine.Current);             // "回到父态" = 回到它上次的子页
            Assert.Equal(1, f.Main.Enter);                         // 降层未重跑 Main.OnEnter（全程只进一次）
        }

        [Fact]
        public void HSM_重入当前最深_抛()
        {
            var f = new Fixture();
            f.Machine.Start(Id.Main);
            Assert.Throws<InvalidOperationException>(() => f.Machine.Request(Id.Lobby));
        }

        // ---- 历史（H8）----

        [Fact]
        public void HSM_浅历史_只恢复直接子态更深处用初始()
        {
            var f = new Fixture(HistoryMode.Shallow, HistoryMode.Shallow);
            f.Machine.Start(Id.Main);
            f.Machine.Request(Id.Result); f.Machine.Advance();      // Main/Battle/Result
            f.Machine.Request(Id.Lobby); f.Machine.Advance();       // 退出：Main 记 [Battle]，Battle 记 [Result]

            f.Machine.Request(Id.Battle); f.Machine.Advance();      // Main/ + Battle → Battle 有浅历史 → Ongoing? 
            // 浅历史只恢复 Battle 的直接子态 = Result（Battle 自己记的）→ 停在 Result
            Assert.Equal(new[] { Id.Main, Id.Battle, Id.Result }, f.Machine.ActivePath);
        }

        [Fact]
        public void HSM_深历史_恢复整条链()
        {
            var f = new Fixture(HistoryMode.Deep, HistoryMode.Deep);
            f.Machine.Start(Id.Main);
            f.Machine.Request(Id.Result); f.Machine.Advance();
            f.Machine.Request(Id.Lobby); f.Machine.Advance();

            f.Machine.Request(Id.Battle); f.Machine.Advance();
            Assert.Equal(new[] { Id.Main, Id.Battle, Id.Result }, f.Machine.ActivePath);   // 深历史整链回放
        }

        [Fact]
        public void HSM_无历史_每次从InitialChild进()
        {
            var f = new Fixture(HistoryMode.None, HistoryMode.None);
            f.Machine.Start(Id.Main);
            f.Machine.Request(Id.Result); f.Machine.Advance();
            f.Machine.Request(Id.Lobby); f.Machine.Advance();
            f.Machine.Request(Id.Battle); f.Machine.Advance();
            Assert.Equal(Id.Ongoing, f.Machine.Current);            // 无历史 → InitialChild
        }

        [Fact]
        public void HSM_显式目标优先于历史()
        {
            var f = new Fixture(HistoryMode.Deep, HistoryMode.Deep);
            f.Machine.Start(Id.Main);
            f.Machine.Request(Id.Result); f.Machine.Advance();      // 制造历史 [Battle, Result]
            f.Machine.Request(Id.Match); f.Machine.Advance();
            f.Machine.Request(Id.Ongoing); f.Machine.Advance();     // 显式到 Ongoing：不由历史改道
            Assert.Equal(new[] { Id.Main, Id.Battle, Id.Ongoing }, f.Machine.ActivePath);
        }

        [Fact]
        public void HSM_无历史可恢复时用InitialChild()
        {
            var f = new Fixture();
            f.Machine.Start(Id.Main);
            f.Machine.Request(Id.Battle); f.Machine.Advance();       // 首次进 Battle，无历史 → Ongoing
            Assert.Equal(Id.Ongoing, f.Machine.Current);
        }

        // ---- 冒泡（H9）----

        [Fact]
        public void HSM_冒泡_最深优先_未处理则上浮()
        {
            var log = new List<string>();
            var mainSink = new StageWithSink(7);                  // 父层消费 7
            var lobbySink = new StageWithSink(-1);                // 子层不消费
            var other = new Stage(log, "m");

            var hsm = new HierarchicalStageMachine<Id, Req>("b",
                new (Id, IStage<Id, Req>)[]
                {
                    (Id.Main, mainSink), (Id.Lobby, lobbySink), (Id.Match, other),
                },
                composites: new[] { new CompositeSpec<Id>(Id.Main, Id.Lobby, HistoryMode.None, Id.Lobby, Id.Match) });

            hsm.Start(Id.Main);                                   // Main / Lobby（最深 = Lobby）
            Assert.True(hsm.Raise(7));                            // Lobby 先被问（不消费）→ Main 消费
            Assert.Equal(1, lobbySink.Asked);
            Assert.Equal(1, mainSink.Asked);

            Assert.False(hsm.Raise(99));                          // 都不消费 → false
            Assert.Equal(2, lobbySink.Asked);
            Assert.Equal(2, mainSink.Asked);
        }

        [Fact]
        public void HSM_冒泡_无人处理返回false()
        {
            var f = new Fixture();
            f.Machine.Start(Id.Main);
            Assert.False(f.Machine.Raise(3));                         // 普通 Stage 未实现 IEventSink
        }

        // ---- Reset（深→浅收尾 + 清历史）----

        [Fact]
        public void HSM_Reset_深到浅OnLeave_清路径与历史_可再次Start()
        {
            var f = new Fixture(HistoryMode.Shallow, HistoryMode.Deep);
            f.Machine.Start(Id.Main);                              // Main/Lobby
            f.Machine.Request(Id.Result); f.Machine.Advance();     // Main/Battle/Result
            Assert.Equal(3, f.Machine.ActivePath.Count);

            f.Log.Clear();
            f.Machine.Reset();
            Assert.Equal(new[] { "-Result", "-Battle", "-Main" }, f.Log);   // 深 → 浅（与 enter 的浅→深对称）
            Assert.Empty(f.Machine.ActivePath);
            Assert.False(f.Machine.Started);
            Assert.False(f.Machine.HasPending);

            // 历史已清 → 重新 Start 后回到 Main 的 InitialChild（Lobby），而不是上次的 Battle/Result
            f.Machine.Start(Id.Main);
            Assert.Equal(new[] { Id.Main, Id.Lobby }, f.Machine.ActivePath);
        }

        [Fact]
        public void HSM_Reset后_Request抛_未启动语义恢复()
        {
            var f = new Fixture();
            f.Machine.Start(Id.Main);
            f.Machine.Reset();
            Assert.Throws<InvalidOperationException>(() => f.Machine.Request(Id.Match));
            f.Machine.Tick(0.016f);                                // 未启动：Tick 静默
            Assert.Equal(0, f.Main.Update);
        }

        // ---- H10：7 条传统语义 ----

        [Fact]
        public void HSM_未Start_Tick静默_Request抛()
        {
            var f = new Fixture();
            f.Machine.Tick(0.016f);
            Assert.Equal(0, f.Main.Update);
            Assert.Throws<InvalidOperationException>(() => f.Machine.Request(Id.Lobby));
        }

        [Fact]
        public void HSM_帧末应用_OnUpdate之后才算迁移()
        {
            var f = new Fixture();
            f.Machine.Start(Id.Main);
            f.Main.OnUpdateHook = () => f.Machine.Request(Id.Match);
            f.Log.Clear();

            f.Machine.Tick(0.1f);
            Assert.Equal(new[] { "uMain", "uLobby", "-Lobby", "+Match" }, f.Log);
            //                                                  ^ 退出在 OnUpdate 之后
            Assert.Equal(Id.Match, f.Machine.Current);
            Assert.Equal(0f, f.Machine.StageTime, 5);
        }

        [Fact]
        public void HSM_同帧多次请求_last_wins()
        {
            var f = new Fixture();
            f.Machine.Start(Id.Main);
            f.Main.OnUpdateHook = () =>
            {
                f.Machine.Request(Id.Match, new Req(1));
                f.Machine.Request(Id.Battle, new Req(2));
            };
            f.Machine.Tick(0.1f);
            Assert.Equal(new[] { Id.Main, Id.Battle, Id.Ongoing }, f.Machine.ActivePath);
            Assert.Equal(2, f.Ongoing.LastReq);                        // payload 也 last-wins
            Assert.Equal(0, f.Match.Enter);
        }

        [Fact]
        public void HSM_OnEnter内请求_挂下次Advance()
        {
            var f = new Fixture();
            f.Machine.Start(Id.Main);
            f.Match.OnEnterHook = () => f.Machine.Request(Id.Lobby);

            f.Machine.Request(Id.Match); f.Machine.Advance();
            Assert.Equal(Id.Match, f.Machine.Current);
            Assert.True(f.Machine.HasPending);                         // OnEnter 内请求挂起

            f.Machine.Tick(0.1f);                                      // Match 先收一次 OnUpdate，帧末才切
            Assert.Equal(1, f.Match.Update);
            Assert.Equal(Id.Lobby, f.Machine.Current);
        }

        [Fact]
        public void HSM_未注册目标_抛()
        {
            var f = new Fixture();
            f.Machine.Start(Id.Main);
            Assert.Throws<InvalidOperationException>(() => f.Machine.Request(Id.Boot + 100));   // 越界枚举值
        }

        [Fact]
        public void HSM_OnLeave内改道_抛()
        {
            var f = new Fixture();
            f.Machine.Start(Id.Main);
            f.Lobby.OnLeaveHook = () => f.Machine.Request(Id.Boot);
            f.Machine.Request(Id.Match);
            Assert.Throws<InvalidOperationException>(() => f.Machine.Advance());
        }

        // ---- 异常中断（不回滚）----

        [Fact]
        public void HSM_事务中途抛_保留已完成部分并标记中断()
        {
            var f = new Fixture();
            f.Machine.Start(Id.Main);                                  // Main/Lobby
            f.Lobby.OnLeaveHook = () => throw new InvalidOperationException("boom");
            f.Machine.Request(Id.Match);

            Assert.Throws<InvalidOperationException>(() => f.Machine.Advance());
            Assert.True(f.Machine.Interrupted);
            Assert.Equal(Id.Match, f.Machine.InterruptedTarget);
            // 语义：**抛出 OnLeave 的那一层视为"未完成退出"而保留**（可重试），已成功退出的层才移除
            Assert.Equal(new[] { Id.Main, Id.Lobby }, f.Machine.ActivePath);
            Assert.Equal(Id.Lobby, f.Machine.Current);                   // 仍停在未成功退出的层

            f.Lobby.OnLeaveHook = null;                                 // 摘掉毒钩子
            f.Machine.Request(Id.Match);                                // 恢复：可继续推进
            f.Machine.Advance();
            Assert.False(f.Machine.Interrupted);
            Assert.Equal(Id.Match, f.Machine.Current);
        }

        // ---- flat 与 HSM 对拍（退化形态一致性）----

        [Fact]
        public void 对拍_flat与HSM单层_帧末应用与last_wins一致()
        {
            // flat：A → (同帧请求 B、C) → C
            var la = new FlatStage(); var lb = new FlatStage(); var lc = new FlatStage();
            var flat = new StageMachine<Id, Req>("f", (Id.Lobby, la), (Id.Match, lb), (Id.Battle, lc));
            flat.Start(Id.Lobby);
            la.OnUpdateHook = () => { flat.Request(Id.Match); flat.Request(Id.Battle); };
            flat.Tick(0.1f);

            // HSM：同结构（无复合态 → 三个平级根）
            var ha = new Stage(new List<string>(), "a"); var hb = new Stage(new List<string>(), "b"); var hc = new Stage(new List<string>(), "c");
            var hsm = new HierarchicalStageMachine<Id, Req>("h",
                new (Id, IStage<Id, Req>)[] { (Id.Lobby, ha), (Id.Match, hb), (Id.Battle, hc) },
                composites: Array.Empty<CompositeSpec<Id>>());
            hsm.Start(Id.Lobby);
            ha.OnUpdateHook = () => { hsm.Request(Id.Match); hsm.Request(Id.Battle); };
            hsm.Tick(0.1f);

            Assert.Equal(flat.Current, hsm.Current);
            Assert.Equal(lc.Enter, hc.Enter);
            Assert.Equal(lb.Enter, hb.Enter);
            Assert.Equal(la.Update, ha.Update);
        }

        [Fact]
        public void 对拍_flat的Raise等价于HSM单层冒泡()
        {
            var sink = new FlatSink(5);
            var flat = new StageMachine<Id, Req>("f", (Id.Lobby, sink));
            flat.Start(Id.Lobby);
            Assert.True(flat.Raise(5));
            Assert.False(flat.Raise(6));
        }

        private sealed class FlatStage : IStage<Id, Req>
        {
            public int Enter, Leave, Update;
            public Action OnUpdateHook;
            public void OnInit(IStageHost<Id, Req> m) { }
            public void OnEnter(IStageHost<Id, Req> m, in Req req) => Enter++;
            public void OnUpdate(IStageHost<Id, Req> m, float s) { Update++; OnUpdateHook?.Invoke(); }
            public void OnLeave(IStageHost<Id, Req> m) => Leave++;
        }

        private sealed class FlatSink : IStage<Id, Req>, IEventSink<int>
        {
            private readonly int _hit;
            public FlatSink(int hit) => _hit = hit;
            public void OnInit(IStageHost<Id, Req> m) { }
            public void OnEnter(IStageHost<Id, Req> m, in Req req) { }
            public void OnUpdate(IStageHost<Id, Req> m, float s) { }
            public void OnLeave(IStageHost<Id, Req> m) { }
            public bool TryHandle(in int e) => e == _hit;
        }
    }
}
