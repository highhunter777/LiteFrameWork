using System;
using LiteFramework;
using Xunit;

namespace LiteFramework.Tests
{
    /// <summary>
    /// 抢占型层级机直测（《状态机专项设计》§3.3/§6 验收）：与平面抢占机同判例——
    /// 最深活动态裁决（优先级/中断规则/恢复意愿）、自身推进放行、恢复栈存 id 走原生展开管线、
    /// TryResume 绕准入、事件边×抢占并存（sink 消费不迁移）、看门狗强制绕准入。
    /// </summary>
    public sealed class PreemptiveHierarchicalStageMachineTests
    {
        private sealed class Evt { }

        private enum Pid { Root, Idle, Atk, Swing1, Swing2, Hurt }

        private static TableStage<Pid, int> Table(Pid id, int priority = 0, bool interruptible = true,
            ResumeMode resume = ResumeMode.Cancel,
            int timeoutFrames = 0, Pid timeoutTarget = default)
            => new TableStage<Pid, int>(new StageSpec<Pid, int>
            {
                Id = id,
                Priority = priority,
                CanBeInterrupted = interruptible,
                Resume = resume,
                TimeoutFrames = timeoutFrames,
                TimeoutTarget = timeoutTarget,
            });

        /// <summary>标准树：Root(复合)→Idle/Atk/Hurt；Atk(复合)→Swing1/Swing2。</summary>
        private static PreemptiveHierarchicalStageMachine<Pid, int> Standard()
            => new PreemptiveHierarchicalStageMachine<Pid, int>("PH",
                new[]
                {
                    (Pid.Root, (IStage<Pid, int>)Table(Pid.Root)),
                    (Pid.Idle, Table(Pid.Idle)),
                    (Pid.Atk, Table(Pid.Atk)),
                    (Pid.Swing1, Table(Pid.Swing1)),
                    (Pid.Swing2, Table(Pid.Swing2)),
                    (Pid.Hurt, Table(Pid.Hurt, priority: 20)),
                },
                new[]
                {
                    new CompositeSpec<Pid>(Pid.Root, Pid.Idle, HistoryMode.Shallow, Pid.Idle, Pid.Atk, Pid.Hurt),
                    new CompositeSpec<Pid>(Pid.Atk, Pid.Swing1, HistoryMode.Shallow, Pid.Swing1, Pid.Swing2),
                });

        [Fact]
        public void 优先级抢占_高优先级切进_低优先级被拒()
        {
            var m = Standard();
            m.Start(Pid.Root);                               // [Root, Idle(0)]
            Assert.Equal(Pid.Idle, m.Current);

            Assert.True(m.Request(Pid.Hurt));                // Hurt(20) ≥ Idle(0)：切进
            m.Advance();
            Assert.Equal(Pid.Hurt, m.Current);               // [Root, Hurt]

            Assert.False(m.Request(Pid.Idle));               // Idle(0) < Hurt(20)：被拒（裁决问最深态）
            Assert.Equal(RejectReason.Priority, m.LastReject);
            Assert.False(m.HasPending);                      // 被拒不改挂起
        }

        [Fact]
        public void 霸体_中断规则拒绝_强制通道绕过()
        {
            var m = new PreemptiveHierarchicalStageMachine<Pid, int>("PH",
                new[]
                {
                    (Pid.Root, (IStage<Pid, int>)Table(Pid.Root)),
                    (Pid.Idle, Table(Pid.Idle)),
                    (Pid.Atk, Table(Pid.Atk, priority: 10, interruptible: false)),   // 霸体
                    (Pid.Hurt, Table(Pid.Hurt, priority: 20)),
                },
                new[] { new CompositeSpec<Pid>(Pid.Root, Pid.Idle, HistoryMode.None, Pid.Idle, Pid.Atk, Pid.Hurt) });
            m.Start(Pid.Root);
            m.Request(Pid.Atk);                              // Atk(10) ≥ Idle(0)
            m.Advance();
            Assert.Equal(Pid.Atk, m.Current);

            Assert.False(m.Request(Pid.Hurt));               // 优先级够但霸体拒绝
            Assert.Equal(RejectReason.InterruptDisallowed, m.LastReject);

            m.ForceState(Pid.Hurt);                          // 强制通道绕准入
            m.Advance();
            Assert.Equal(Pid.Hurt, m.Current);
        }

        [Fact]
        public void 自身推进放行_钩子内低优先级切换成功()
        {
            PreemptiveHierarchicalStageMachine<Pid, int> m = null;
            var swing = Table(Pid.Swing2, priority: 10);
            swing.Spec.OnUpdateAction = _ => m.Request(Pid.Idle);    // 钩子内"回 Idle"（0 < 10）
            m = new PreemptiveHierarchicalStageMachine<Pid, int>("PH",
                new[]
                {
                    (Pid.Root, (IStage<Pid, int>)Table(Pid.Root)),
                    (Pid.Idle, Table(Pid.Idle)),
                    (Pid.Atk, Table(Pid.Atk, priority: 10)),
                    (Pid.Swing1, Table(Pid.Swing1)),
                    (Pid.Swing2, swing),
                },
                new[]
                {
                    new CompositeSpec<Pid>(Pid.Root, Pid.Idle, HistoryMode.None, Pid.Idle, Pid.Atk),
                    new CompositeSpec<Pid>(Pid.Atk, Pid.Swing1, HistoryMode.None, Pid.Swing1, Pid.Swing2),
                });
            m.Start(Pid.Root);
            m.Request(Pid.Swing2);                           // Swing2(10) ≥ Idle(0)
            m.Advance();
            Assert.Equal(Pid.Swing2, m.Current);

            m.Tick(0.016f);                                  // OnUpdate 内 Request(Idle)——自身推进放行，帧末应用
            Assert.Equal(Pid.Idle, m.Current);
        }

        [Fact]
        public void 被抢占入恢复栈_TryResume绕准入回原路径()
        {
            var m = Standard();
            m.Start(Pid.Root);
            m.Request(Pid.Swing2);
            m.Advance();
            Assert.Equal(Pid.Swing2, m.Current);

            Assert.True(m.Request(Pid.Hurt));                // 抢占（20 ≥ 0）：最深态 Swing2 = Cancel 不入栈
            m.Advance();
            Assert.Equal(Pid.Hurt, m.Current);
            Assert.Equal(0, m.ResumeDepth);

            var m3 = new PreemptiveHierarchicalStageMachine<Pid, int>("PH3",
                new[]
                {
                    (Pid.Root, (IStage<Pid, int>)Table(Pid.Root)),
                    (Pid.Idle, Table(Pid.Idle)),
                    (Pid.Atk, Table(Pid.Atk, priority: 10, resume: ResumeMode.Resume)),   // 最深态 + 要恢复
                    (Pid.Hurt, Table(Pid.Hurt, priority: 20)),
                },
                new[] { new CompositeSpec<Pid>(Pid.Root, Pid.Idle, HistoryMode.None, Pid.Idle, Pid.Atk, Pid.Hurt) });
            m3.Start(Pid.Root);
            m3.Request(Pid.Atk);
            m3.Advance();
            Assert.Equal(Pid.Atk, m3.Current);

            Assert.True(m3.Request(Pid.Hurt));
            m3.Advance();
            Assert.Equal(1, m3.ResumeDepth);                 // Atk 入栈

            Assert.True(m3.TryResume());                     // 绕准入（Atk(10) < Hurt(20) 也回）
            m3.Advance();
            Assert.Equal(Pid.Atk, m3.Current);               // 路径由树反查展开：[Root, Atk]
            Assert.Equal(0, m3.ResumeDepth);
        }

        [Fact]
        public void TryResume_栈空与已在目标()
        {
            var m = Standard();
            m.Start(Pid.Root);

            Assert.False(m.TryResume());                     // 空栈
            Assert.Equal(RejectReason.ResumeStackEmpty, m.LastReject);

            Assert.True(m.Request(Pid.Hurt));
            m.Advance();
            Assert.False(m.TryResume());                     // 仍是空栈（被抢占的 Idle 是 Cancel）
            Assert.Equal(RejectReason.ResumeStackEmpty, m.LastReject);

            // 已在目标：S1(Resume) 被 S2 抢占入栈 → 切回 S1 → 栈顶 == 当前 → 丢弃该条
            var m2 = new PreemptiveHierarchicalStageMachine<Pid, int>("PH2",
                new[]
                {
                    (Pid.Swing1, (IStage<Pid, int>)Table(Pid.Swing1, resume: ResumeMode.Resume)),
                    (Pid.Swing2, Table(Pid.Swing2)),
                },
                null);
            m2.Start(Pid.Swing1);
            Assert.True(m2.Request(Pid.Swing2));             // 同值可互相抢占
            m2.Advance();
            Assert.Equal(1, m2.ResumeDepth);                 // Swing1 入栈
            Assert.True(m2.Request(Pid.Swing1));
            m2.Advance();
            Assert.Equal(Pid.Swing1, m2.Current);

            Assert.False(m2.TryResume());                    // 栈顶 Swing1 == Current：丢弃
            Assert.Equal(RejectReason.ResumeAlreadyCurrent, m2.LastReject);
            Assert.Equal(0, m2.ResumeDepth);
        }

        [Fact]
        public void 恢复栈深度上限_丢最老并计数()
        {
            // 平铺根（无复合）：每次迁移旧最深态（声明 Resume）入栈，第 5 条丢最老
            var m = new PreemptiveHierarchicalStageMachine<Pid, int>("PH",
                new[]
                {
                    (Pid.Swing1, (IStage<Pid, int>)Table(Pid.Swing1, resume: ResumeMode.Resume)),
                    (Pid.Swing2, Table(Pid.Swing2, resume: ResumeMode.Resume)),
                    (Pid.Atk, Table(Pid.Atk, resume: ResumeMode.Resume)),
                    (Pid.Hurt, Table(Pid.Hurt, resume: ResumeMode.Resume)),
                    (Pid.Idle, Table(Pid.Idle, resume: ResumeMode.Resume)),
                    (Pid.Root, Table(Pid.Root, priority: 99)),
                },
                null);
            m.Start(Pid.Swing1);

            foreach (var next in new[] { Pid.Swing2, Pid.Atk, Pid.Hurt, Pid.Idle })
            {
                Assert.True(m.Request(next));                // 同值(0)互相放行
                m.Advance();
            }
            Assert.Equal(4, m.ResumeDepth);                  // [Swing1, Swing2, Atk, Hurt]

            Assert.True(m.Request(Pid.Root));                // Root(99) 抢占；Idle 入栈时丢最老（Swing1）
            m.Advance();
            Assert.Equal(4, m.ResumeDepth);
            Assert.Equal(1, m.ResumeDropped);
        }

        [Fact]
        public void 事件边命中_准入拒绝仍消费_不冒泡()
        {
            var t = new TransitionTable<Pid, int>();
            t.AddEdge<Evt>(Pid.Hurt, Pid.Idle);              // Hurt(20) --Evt--> Idle(0)：优先级不够
            var m = new PreemptiveHierarchicalStageMachine<Pid, int>("PH",
                new[]
                {
                    (Pid.Root, (IStage<Pid, int>)Table(Pid.Root)),
                    (Pid.Idle, Table(Pid.Idle)),
                    (Pid.Atk, Table(Pid.Atk)),
                    (Pid.Swing1, Table(Pid.Swing1)),
                    (Pid.Swing2, Table(Pid.Swing2)),
                    (Pid.Hurt, Table(Pid.Hurt, priority: 20)),
                },
                new[]
                {
                    new CompositeSpec<Pid>(Pid.Root, Pid.Idle, HistoryMode.None, Pid.Idle, Pid.Atk, Pid.Hurt),
                    new CompositeSpec<Pid>(Pid.Atk, Pid.Swing1, HistoryMode.None, Pid.Swing1, Pid.Swing2),
                },
                t);
            m.Start(Pid.Root);
            m.Request(Pid.Hurt);
            m.Advance();
            Assert.Equal(Pid.Hurt, m.Current);

            Assert.True(m.Raise(new Evt()));                 // 边命中 = 机器已裁决（消费——返回 true）
            Assert.Equal(RejectReason.Priority, m.LastReject);   // 但准入拒绝：迁移不发生
            Assert.Equal(Pid.Hurt, m.Current);
            Assert.False(m.HasPending);
        }

        [Fact]
        public void 冒泡sink消费_不产生迁移()
        {
            var sinkStage = new SinkRoot();
            var m = new PreemptiveHierarchicalStageMachine<Pid, int>("PH",
                new[] { (Pid.Root, (IStage<Pid, int>)sinkStage), (Pid.Idle, Table(Pid.Idle)) },
                new[] { new CompositeSpec<Pid>(Pid.Root, Pid.Idle, HistoryMode.None, Pid.Idle) });
            m.Start(Pid.Root);
            Assert.Equal(Pid.Idle, m.Current);

            Assert.True(m.Raise(new Evt()));                 // 冒泡到 Root 的 sink，消费
            Assert.Equal(Pid.Idle, m.Current);               // sink 消费 ≠ 迁移
            Assert.Equal(1, sinkStage.Handled);
        }

        private sealed class SinkRoot : IStage<Pid, int>, IEventSink<Evt>
        {
            public int Handled;
            public void OnInit(IStageHost<Pid, int> m) { }
            public void OnEnter(IStageHost<Pid, int> m, in int req) { }
            public void OnUpdate(IStageHost<Pid, int> m, float elapse) { }
            public void OnLeave(IStageHost<Pid, int> m) { }
            public bool TryHandle(in Evt e) { Handled++; return true; }
        }

        [Fact]
        public void 看门狗_抢占层级机上强制绕准入()
        {
            var m = new PreemptiveHierarchicalStageMachine<Pid, int>("PH",
                new[]
                {
                    (Pid.Root, (IStage<Pid, int>)Table(Pid.Root)),
                    (Pid.Idle, Table(Pid.Idle)),
                    (Pid.Atk, Table(Pid.Atk, priority: 10, interruptible: false, timeoutFrames: 3, timeoutTarget: Pid.Idle)),
                },
                new[] { new CompositeSpec<Pid>(Pid.Root, Pid.Idle, HistoryMode.None, Pid.Idle, Pid.Atk) });
            m.Start(Pid.Root);
            m.Request(Pid.Atk);
            m.Advance();
            Assert.Equal(Pid.Atk, m.Current);

            Assert.False(m.Request(Pid.Idle));               // 霸体拒绝
            m.Tick(0.016f);
            m.Tick(0.016f);
            Assert.Equal(Pid.Atk, m.Current);
            m.Tick(0.016f);                                  // 第 3 帧：看门狗 ForceState 绕霸体
            Assert.Equal(Pid.Idle, m.Current);
            Assert.Equal(1, m.TimeoutCount);
        }
    }
}
