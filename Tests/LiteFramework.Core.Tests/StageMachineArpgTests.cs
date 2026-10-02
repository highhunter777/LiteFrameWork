using System;
using System.Collections.Generic;
using Xunit;

namespace LiteFramework.Tests
{
    /// <summary>
    /// 状态机 **ARPG 形态扩展**：表驱动（`StageSpec` + `TableStage`）/ 优先级抢占 /
    /// 恢复栈 / 帧窗口；外加"约束放宽"（`TId` 只要求 `struct`——这里用 `int` 作技能 id）。
    /// 语义编号对应设计 §2 的 N1–N8。
    /// </summary>
    public sealed class StageMachineArpgTests
    {
        // TId = int：表驱动的"技能/动作 id"（集合不编译期固定）
        private const int Idle = 0;
        private const int Move = 1;
        private const int Attack = 2;
        private const int Attack2 = 3;
        private const int HitStun = 4;
        private const int Ultimate = 5;
        private const int Dodge = 6;

        private readonly struct Req
        {
            public readonly int Value;
            public Req(int value) => Value = value;
        }

        private static StageSpec<int, Req> Spec(int id, int priority = 0, int duration = 0, int next = Idle, bool hasNext = false)
            => new StageSpec<int, Req> { Id = id, Priority = priority, DurationFrames = duration, NextId = next, HasNext = hasNext };

        /// <summary>抢占机（表驱动 + 优先级 + 恢复栈）—— ARPG 形态用这个；基础机（无抢占）另有对拍用例。</summary>
        private static PreemptiveStageMachine<int, Req> Machine(params StageSpec<int, Req>[] specs)
        {
            var stages = new (int, IStage<int, Req>)[specs.Length];
            for (int i = 0; i < specs.Length; i++) stages[i] = (specs[i].Id, new TableStage<int, Req>(specs[i]));
            return new PreemptiveStageMachine<int, Req>("arpg", stages);
        }

        // ---- 表驱动（int 作 id）----

        [Fact]
        public void 表驱动_int作Id_进入更新离开与钩子()
        {
            int enters = 0, updates = 0, leaves = 0;
            var idle = Spec(Idle);
            var move = Spec(Move);
            move.OnEnterAction = _ => enters++;
            move.OnUpdateAction = _ => updates++;
            move.OnLeaveAction = _ => leaves++;

            var m = Machine(idle, move);
            m.Start(Idle);
            Assert.Equal(Idle, m.Current);

            Assert.True(m.Request(Move, new Req(7)));
            m.Tick(0.016f);                                   // 帧末应用
            Assert.Equal(Move, m.Current);
            Assert.Equal(1, enters);

            m.Tick(0.016f);
            Assert.Equal(1, updates);                         // 迁移那一帧不调新阶段的 OnUpdate（帧末才迁）

            Assert.True(m.Request(Idle));
            m.Tick(0.016f);
            Assert.Equal(1, leaves);
            Assert.Equal(Idle, m.Current);
        }

        [Fact]
        public void 表驱动_时长到点自动迁移()
        {
            var idle = Spec(Idle);
            var attack = Spec(Attack, duration: 3, next: Idle, hasNext: true);
            var m = Machine(idle, attack);
            m.Start(Idle);
            m.Request(Attack);
            m.Tick(0.016f);                                   // → Attack（帧数归零）
            Assert.Equal(Attack, m.Current);

            m.Tick(0.016f);                                   // 帧 1
            m.Tick(0.016f);                                   // 帧 2
            Assert.Equal(Attack, m.Current);
            m.Tick(0.016f);                                   // 帧 3 → 到点，自身推进（不受优先级约束）
            Assert.Equal(Idle, m.Current);
        }

        [Fact]
        public void 表驱动_时长到点但未配下一步_停在原地()
        {
            var idle = Spec(Idle);
            var attack = Spec(Attack, duration: 2);           // HasNext 未设
            var m = Machine(idle, attack);
            m.Start(Idle);
            m.Request(Attack);
            for (int i = 0; i < 6; i++) m.Tick(0.016f);
            Assert.Equal(Attack, m.Current);
        }

        // ---- 优先级 / 抢占（N1/N2/N3）----

        [Fact]
        public void 优先级_外部请求高优先级可抢占()
        {
            var idle = Spec(Idle);
            var attack = Spec(Attack, priority: 10);
            var hit = Spec(HitStun, priority: 20);
            var m = Machine(idle, attack, hit);
            m.Start(Idle);
            m.Request(Attack); m.Tick(0.016f);
            Assert.Equal(Attack, m.Current);

            Assert.True(m.Request(HitStun));                  // 外部：20 ≥ 10 → 可抢占
            m.Tick(0.016f);
            Assert.Equal(HitStun, m.Current);
        }

        [Fact]
        public void 优先级_外部请求低优先级被拒_返回false且挂起不变()
        {
            var idle = Spec(Idle);                            // priority 0
            var attack = Spec(Attack, priority: 10);
            var m = Machine(idle, attack);
            m.Start(Idle);
            m.Request(Attack); m.Tick(0.016f);

            Assert.False(m.Request(Idle));                    // 外部：0 < 10 → 拒（N1：返回 false，不抛）
            Assert.False(m.HasPending);                       // N1：不改挂起
            m.Tick(0.016f);
            Assert.Equal(Attack, m.Current);
        }

        [Fact]
        public void 优先级_同优先级可互相迁移()
        {
            var idle = Spec(Idle);
            var a1 = Spec(Attack, priority: 10);
            var a2 = Spec(Attack2, priority: 10);
            var m = Machine(idle, a1, a2);
            m.Start(Idle);
            m.Request(Attack); m.Tick(0.016f);

            Assert.True(m.Request(Attack2));                  // 10 ≥ 10 → 允许（连段）
            m.Tick(0.016f);
            Assert.Equal(Attack2, m.Current);
        }

        [Fact]
        public void 霸体_不可打断时拒绝一切()
        {
            var idle = Spec(Idle);
            var ultimate = Spec(Ultimate, priority: 5);
            ultimate.CanBeInterrupted = false;                // 霸体
            var hit = Spec(HitStun, priority: 99);
            var m = Machine(idle, ultimate, hit);
            m.Start(Idle);
            m.Request(Ultimate); m.Tick(0.016f);

            Assert.False(m.Request(HitStun));                 // 优先级再高也拒（CanBeInterrupted=false）
            m.Tick(0.016f);
            Assert.Equal(Ultimate, m.Current);
        }

        [Fact]
        public void 细粒度规则_CanBeInterruptedBy生效()
        {
            var idle = Spec(Idle);
            var attack = Spec(Attack, priority: 10);
            attack.CanBeInterruptedBy = incoming => incoming == HitStun;   // 只允许受击打断
            var dodge = Spec(Dodge, priority: 20);
            var hit = Spec(HitStun, priority: 20);
            var m = Machine(idle, attack, dodge, hit);
            m.Start(Idle);
            m.Request(Attack); m.Tick(0.016f);

            Assert.False(m.Request(Dodge));                   // 优先级够，但规则不允许
            Assert.True(m.Request(HitStun));                  // 规则允许
            m.Tick(0.016f);
            Assert.Equal(HitStun, m.Current);
        }

        [Fact]
        public void 请求覆盖_priorityOverride临时越级()
        {
            var idle = Spec(Idle);
            var attack = Spec(Attack, priority: 10);
            var m = Machine(idle, attack);
            m.Start(Idle);
            m.Request(Attack); m.Tick(0.016f);

            Assert.False(m.Request(Idle));                                  // 正常路径：被拒
            Assert.True(m.Request(Idle, new Req(0), priorityOverride: 999)); // N3：本次覆盖 → 允许
            m.Tick(0.016f);
            Assert.Equal(Idle, m.Current);
        }

        // ---- 恢复栈（N4/N5/N6/N7）----

        [Fact]
        public void 恢复栈_Resume模式被抢占后入栈_可迁回()
        {
            var idle = Spec(Idle);
            var attack = Spec(Attack, priority: 10);
            attack.Resume = ResumeMode.Resume;                // 被打断后要能回来
            var hit = Spec(HitStun, priority: 20);
            var m = Machine(idle, attack, hit);
            m.Start(Idle);
            m.Request(Attack); m.Tick(0.016f);

            m.Request(HitStun); m.Tick(0.016f);               // 受击抢占 Attack
            Assert.Equal(HitStun, m.Current);
            Assert.True(m.HasResumePending);                  // N4：入栈
            Assert.Equal(1, m.ResumeDepth);

            Assert.True(m.TryResume());                       // N6：弹栈迁回
            m.Tick(0.016f);
            Assert.Equal(Attack, m.Current);
            Assert.False(m.HasResumePending);
        }

        [Fact]
        public void 恢复栈_Cancel模式不入栈()
        {
            var idle = Spec(Idle);
            var move = Spec(Move, priority: 10);              // 默认 ResumeMode.Cancel
            var hit = Spec(HitStun, priority: 20);
            var m = Machine(idle, move, hit);
            m.Start(Idle);
            m.Request(Move); m.Tick(0.016f);

            m.Request(HitStun); m.Tick(0.016f);
            Assert.Equal(HitStun, m.Current);
            Assert.False(m.HasResumePending);                 // 不入栈
            Assert.False(m.TryResume());                      // 栈空
        }

        [Fact]
        public void 恢复栈_超深丢最老并计数()
        {
            var idle = Spec(Idle);
            var resumeStages = new List<StageSpec<int, Req>>();
            var specs = new List<StageSpec<int, Req>> { idle };
            const int Base = 100;
            for (int i = 0; i < 6; i++)                        // 6 个 Resume 阶段（上限 4）
            {
                var s = Spec(Base + i, priority: 10 * (i + 1));
                s.Resume = ResumeMode.Resume;
                specs.Add(s); resumeStages.Add(s);
            }
            var m = Machine(specs.ToArray());
            m.Start(Idle);

            for (int i = 0; i < 6; i++)
            {
                m.Request(resumeStages[i].Id); m.Tick(0.016f);
            }
            Assert.Equal(PreemptiveStageMachine<int, Req>.MaxResumeDepth, m.ResumeDepth);   // N5：封顶
            // 6 次请求里只有 5 次"抢占 Resume 阶段"（首次 Idle→S0 的 Idle 不具 Resume）→ 恰好丢 1 个最老
            Assert.Equal(1, m.ResumeDropped);
        }

        [Fact]
        public void 恢复栈_AutoResumeOnEnd优先恢复栈顶()
        {
            var idle = Spec(Idle);
            var attack = Spec(Attack, priority: 10);
            attack.Resume = ResumeMode.Resume;
            var hit = Spec(HitStun, priority: 20, duration: 2);   // 硬直 2 帧
            hit.AutoResumeOnEnd = true;                            // 硬直结束 → 回到被打断的动作（N7）
            var m = Machine(idle, attack, hit);
            m.Start(Idle);
            m.Request(Attack); m.Tick(0.016f);
            m.Request(HitStun); m.Tick(0.016f);
            Assert.Equal(HitStun, m.Current);

            m.Tick(0.016f);                                        // 帧 1
            m.Tick(0.016f);                                        // 帧 2 → 到点 → 恢复栈顶
            Assert.Equal(Attack, m.Current);
            Assert.False(m.HasResumePending);
        }

        // ---- 帧窗口（N8）----

        [Fact]
        public void 帧窗口_StageFrames递增_迁移后归零()
        {
            var idle = Spec(Idle);
            var attack = Spec(Attack);
            var m = Machine(idle, attack);
            m.Start(Idle);
            Assert.Equal(0, m.StageFrames);

            m.Tick(0.016f);
            m.Tick(0.016f);
            Assert.Equal(2, m.StageFrames);                   // N8：每次 Tick +1（不是 dt 累加）

            m.Request(Attack); m.Tick(0.016f);
            Assert.Equal(Attack, m.Current);
            Assert.Equal(0, m.StageFrames);                   // 迁移后归零
        }

        [Fact]
        public void 帧窗口_阶段内可用StageFrames做前N帧判定()
        {
            // 表驱动阶段的钩子里能读到"当前驻留帧数"（取消窗口的判据）
            int cancelledAtFrame = -1;
            var idle = Spec(Idle);
            var attack = Spec(Attack, duration: 5, next: Idle, hasNext: true);
            attack.OnUpdateAction = _ => { };                 // 钩子（不需要 host）
            var m = Machine(idle, attack);
            m.Start(Idle);
            m.Request(Attack); m.Tick(0.016f);

            // 用机器自身的 StageFrames 模拟"第 2 帧之内可取消"
            m.Tick(0.016f);                                   // 帧 1
            if (m.StageFrames <= 2) cancelledAtFrame = m.StageFrames;
            m.Tick(0.016f);                                   // 帧 2
            if (m.StageFrames <= 2) cancelledAtFrame = m.StageFrames;
            Assert.Equal(2, cancelledAtFrame);
        }

        // ---- 被拒原因（RejectReason）----

        [Fact]
        public void 拒绝原因_优先级不足_被接受后清空()
        {
            var idle = Spec(Idle);
            var attack = Spec(Attack, priority: 10);
            var m = Machine(idle, attack);
            m.Start(Idle);
            m.Request(Attack); m.Tick(0.016f);

            Assert.False(m.Request(Idle));
            Assert.Equal(RejectReason.Priority, m.LastReject);          // 为什么放不出来：优先级不足

            Assert.True(m.Request(Idle, new Req(0), priorityOverride: 999));
            Assert.Equal(RejectReason.None, m.LastReject);              // 被接受 → 原因清空（恒反映最近一次）
            m.Tick(0.016f);
            Assert.Equal(Idle, m.Current);
        }

        [Fact]
        public void 拒绝原因_中断规则不允许()
        {
            var idle = Spec(Idle);
            var ultimate = Spec(Ultimate, priority: 5);
            ultimate.CanBeInterrupted = false;                         // 霸体
            var hit = Spec(HitStun, priority: 99);
            var m = Machine(idle, ultimate, hit);
            m.Start(Idle);
            m.Request(Ultimate); m.Tick(0.016f);

            Assert.False(m.Request(HitStun));
            Assert.Equal(RejectReason.InterruptDisallowed, m.LastReject);   // 优先级够但规则不允许
        }

        [Fact]
        public void 拒绝原因_恢复栈空与已在目标()
        {
            var idle = Spec(Idle);
            var attack = Spec(Attack, priority: 10);
            attack.Resume = ResumeMode.Resume;                         // 被打断后入栈
            var hit = Spec(HitStun, priority: 20);
            var m = Machine(idle, attack, hit);
            m.Start(Idle);
            m.Request(Attack); m.Tick(0.016f);

            Assert.False(m.TryResume());
            Assert.Equal(RejectReason.ResumeStackEmpty, m.LastReject);  // 栈空

            m.Request(HitStun); m.Tick(0.016f);                        // 受击抢占 → 栈=[Attack]
            Assert.True(m.HasResumePending);

            // 用覆盖优先级回到 Attack（此时栈里仍有 Attack）→ TryResume 发现"已在目标"
            m.Request(Attack, new Req(0), priorityOverride: 999);
            m.Tick(0.016f);
            Assert.Equal(Attack, m.Current);
            Assert.True(m.HasResumePending);

            Assert.False(m.TryResume());
            Assert.Equal(RejectReason.ResumeAlreadyCurrent, m.LastReject);
            Assert.False(m.HasResumePending);                          // 栈顶那条被丢弃
        }

        // ---- Reset（停止并回未启动态）----

        [Fact]
        public void 抢占机_Reset_清空恢复栈与丢弃计数()
        {
            var idle = Spec(Idle);
            var attack = Spec(Attack, priority: 10);
            attack.Resume = ResumeMode.Resume;
            var hit = Spec(HitStun, priority: 20);
            var m = Machine(idle, attack, hit);
            m.Start(Idle);
            m.Request(Attack); m.Tick(0.016f);
            m.Request(HitStun); m.Tick(0.016f);
            Assert.True(m.HasResumePending);

            m.Reset();
            Assert.False(m.HasResumePending);
            Assert.Equal(0, m.ResumeDepth);
            Assert.Equal(0, m.ResumeDropped);
            Assert.False(m.Started);

            m.Start(Idle);                                             // 可再次 Start
            Assert.Equal(Idle, m.Current);
            Assert.False(m.HasResumePending);
        }

        // ---- 能力分层：基础机没有抢占/恢复（用基础机的人不必理解 ARPG 概念）----

        [Fact]
        public void 基础机_无抢占_低优先级请求也被接受()
        {
            // 同样的规格，但装在**基础机**上 → 优先级字段不生效（无抢占）
            var idle = Spec(Idle);
            var attack = Spec(Attack, priority: 10);
            var baseStages = new (int, IStage<int, Req>)[]
            {
                (Idle, new TableStage<int, Req>(idle)),
                (Attack, new TableStage<int, Req>(attack)),
            };
            var m = new StageMachine<int, Req>("plain", baseStages);
            m.Start(Idle);
            m.Request(Attack); m.Tick(0.016f);
            Assert.Equal(Attack, m.Current);

            Assert.True(m.Request(Idle));                     // 基础机恒接受（0 < 10 也放行）
            m.Tick(0.016f);
            Assert.Equal(Idle, m.Current);
        }

        [Fact]
        public void 能力分层_快照项数区分基础机与抢占机()
        {
            var idle = Spec(Idle);
            var spec = new StageSpec<int, Req> { Id = Idle };
            var d1 = new Dictionary<string, string>();
            var plain = new StageMachine<int, Req>("plain", (Idle, new TableStage<int, Req>(spec)));
            plain.Start(Idle);
            ((IModuleStats)plain).Snapshot(d1);
            Assert.False(d1.ContainsKey("恢复栈深"));          // 基础机无恢复栈概念

            var d2 = new Dictionary<string, string>();
            var pre = new PreemptiveStageMachine<int, Req>("pre", (Idle, new TableStage<int, Req>(spec)));
            pre.Start(Idle);
            ((IModuleStats)pre).Snapshot(d2);
            Assert.True(d2.ContainsKey("恢复栈深"));
            Assert.True(d2.ContainsKey("丢弃恢复"));
            Assert.True(d2.ContainsKey("当前阶段"));            // base 的项仍在（override 先调 base）
        }

        // ---- 约束放宽（Id 可为 struct）----

        [Fact]
        public void 约束放宽_自定义结构体也可作Id()
        {
            var spec = new StageSpec<Key, Req> { Id = new Key(1) };
            var m = new StageMachine<Key, Req>("k", (spec.Id, new TableStage<Key, Req>(spec)));
            m.Start(new Key(1));
            Assert.Equal(1, m.Current.Value);
        }

        /// <summary>自定义结构体 id（框架约束为 `struct`）。</summary>
        private readonly struct Key : IEquatable<Key>
        {
            public readonly int Value;
            public Key(int value) => Value = value;
            public bool Equals(Key other) => Value == other.Value;
            public override bool Equals(object obj) => obj is Key k && Equals(k);
            public override int GetHashCode() => Value;
        }
    }
}
