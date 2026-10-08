using System.Collections.Generic;
using LiteSim;
using RoomServer;
using RoomServer.Runtime;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 延迟补偿（服务器回溯）用例（《M10实施指导》§2.8 / §3"回溯判定"组 / §9 验收行）。
    ///
    /// 验收口径：<b>高 ping（模拟 150ms）下开火命中"客户端看到的那个目标位置"，而不是目标现在的位置</b>。
    /// 形态：直接驱动 <see cref="LagCompensator"/>（不跑网络），历史输入与快照环都按服务器真实时序喂进去。
    /// </summary>
    public sealed class LagCompensationTests
    {
        private const int PlayerCount = 2;

        private static SimMapData BuildMap()
        {
            var map = new SimMapData { GroundY = 0f, HalfWidth = 50f, HalfDepth = 50f };
            map.SpawnPoints[0] = new SimVector3(0f, 0f, 0f);       // 射手：世界原点，朝 +X
            map.SpawnPoints[1] = new SimVector3(20f, 0f, 0f);      // 目标：+X 20m
            map.SpawnPointCount = 2;
            return map;
        }

        /// <summary>把"两人都在动"的输入喂一步（EntityId 必带——缺省 0 会被判失效实体而整帧不生效）。
        /// <paramref name="shooterAimY"/>：射手瞄准点高度（AimPoint 单口径；缺省 1 = 眼高 ⇒ 弹道水平）；
        /// <paramref name="shooterAimX"/>：瞄准点 X（缺省 50 = 远端——移动目标用例要射线延伸过历史位）。</summary>
        private static SimInputFrame[] StepWorld(SimWorldState s, SimMapData map, SnapshotRing ring, LagCompensator lag,
            float shooterMoveX, float targetMoveX, float shooterAimY = 1f, float shooterAimX = 50f)
        {
            var inputs = new SimInputFrame[PlayerCount];
            inputs[0] = new SimInputFrame { EntityId = s.Entities[0].Id, MoveX = shooterMoveX, AimPointX = shooterAimX, AimPointY = shooterAimY, AimPointZ = 0f };
            inputs[1] = new SimInputFrame { EntityId = s.Entities[1].Id, MoveX = targetMoveX };
            SimStep.Step(s, map, inputs, CombatValues.Default, WeaponTable.Default);
            ring.Capture(s.Frame, s);
            lag.RecordInputs(s.Frame, inputs);
            return inputs;
        }

        /// <summary>远距爆头的瞄准点高度：目标轴 X=20 处高度 1.8m，落在头部带 [1.55, 2.0]。</summary>
        private const float HeadAimY = 1.8f;

        /// <summary>是否产出 <paramref name="kind"/> 事件。</summary>
        private static bool HasEvent(SimWorldState s, FrameEventKind kind)
        {
            for (int i = 0; i < s.Events.Count; i++)
                if (s.Events.Items[i].Kind == kind) return true;
            return false;
        }

        [Fact]
        public void 高延迟开火_命中客户端所见位置_而非目标当前位置()
        {
            var map = BuildMap();
            var state = new SimWorldState { RngState = 7UL };
            state.Spawn(new EntitySlot { Hp = CombatConfig.EntityHp, Pos = map.SpawnPoints[0] }, out _);
            state.Spawn(new EntitySlot { Hp = CombatConfig.EntityHp, Pos = map.SpawnPoints[1] }, out _);
            var ring = new SnapshotRing(SimConfig.LagCompHistory);
            var lag = new LagCompensator(state, map, PlayerCount, ring, CombatValues.Default, WeaponTable.Default);

            // 目标沿 +X 匀速远离（射手不动），跑 20 帧（> 窗口 16，保证"当时位置"仍在窗口内）
            // 余量是必须的：目标"当时的位置"也要落在窗口内，否则回溯目标帧已在环外，
            // 判定会退化为当前帧——那是窗口设计的正确行为，不是本用例要验的场景
            const int ExtraFrames = 20;
            for (int i = 0; i < SimConfig.LagCompHistory + ExtraFrames; i++)
                StepWorld(state, map, ring, lag, 0f, 1f);

            int serverFrame = state.Frame;                                  // 服务器当前帧
            float targetNow = state.Entities[1].Pos.X;

            // 客户端看到的是 150ms 前的画面（≈9 帧 @60Hz）+ 插值延迟（§3.4.1：插值 2×快照间隔 = InterpFrames）
            int clientLagFrames = 9;
            int ack = serverFrame - clientLagFrames;                   // 它最后收到的快照帧
            int reportedView = ack + SimConfig.InterpFrames;           // viewFrame = ack 对应权威帧 + 插值帧（协议语义）

            // 服务器回溯目标帧 = reportedView − InterpFrames = ack（= 玩家真正渲染的那一帧）
            int expectedTargetFrame = ack;
            float targetSeen = 20f + expectedTargetFrame * CombatConfig.MoveSpeed * SimConfig.Dt;
            float targetNowExpected = targetNow;

            LagCompensator.Outcome outcome = lag.CompensateFire(0, state.Entities[0].Id, reportedView, ack);

            Assert.Equal(LagCompensator.Outcome.Compensated, outcome);
            Assert.Equal(expectedTargetFrame, lag.LastTargetFrame);

            // 权威态必须毫发无损地还原（回溯不改现在）
            Assert.Equal(serverFrame, state.Frame);
            Assert.Equal(targetNow, state.Entities[1].Pos.X);

            // 决定性判据（§9 验收）：回溯用的是**历史帧的目标位置**，不是当前位置
            // 回溯帧上目标更近（22.25m vs 现在的 <targetNow>）——差值 = 9 帧 × 5m/s × 1/60 ≈ 0.75m
            float seenDistance = targetSeen - state.Entities[0].Pos.X;
            float nowDistance = targetNowExpected - state.Entities[0].Pos.X;
            Assert.True(seenDistance < nowDistance,
                $"回溯应使用更早（更近）的历史位置：seen={seenDistance:F3} now={nowDistance:F3}");
            Assert.True(nowDistance - seenDistance > 0.5f,
                $"回溯位置与当前位置应有可观测差距：Δ={nowDistance - seenDistance:F3}（期望 ≈0.75）");
            Assert.True(seenDistance < CombatConfig.HitscanRange, "历史位置应在射程内");

            // 命令缓冲里必须有一条 Damage，且伤害目标 = 目标实体（命中判定确实在历史态上做出）
            bool damageFound = false;
            long targetId = state.Entities[1].Id;
            for (int i = 0; i < state.Cmds.Count; i++)
                if (state.Cmds.Items[i].Kind == SimCommandKind.Damage && state.Cmds.Items[i].Target == targetId) damageFound = true;
            Assert.True(damageFound, "回溯判定应产出对目标的伤害命令（落到当前帧缓冲）");
        }

        [Fact]
        public void 窗口外开火_退化为当前帧判定()
        {
            var map = BuildMap();
            var state = new SimWorldState();
            state.Spawn(new EntitySlot { Hp = CombatConfig.EntityHp, Pos = map.SpawnPoints[0] }, out _);
            state.Spawn(new EntitySlot { Hp = CombatConfig.EntityHp, Pos = map.SpawnPoints[1] }, out _);
            var ring = new SnapshotRing(SimConfig.LagCompHistory);
            var lag = new LagCompensator(state, map, PlayerCount, ring, CombatValues.Default, WeaponTable.Default);

            for (int i = 0; i < 3; i++) StepWorld(state, map, ring, lag, 0f, 1f);

            // 上报的视角帧远早于环窗口（窗口 16 帧）→ clamp 到窗口下界后仍无历史 → 退化
            int reportedView = state.Frame - (SimConfig.LagCompHistory + 10);   // 远早于窗口下界（clamp 后仍无历史）
            LagCompensator.Outcome outcome = lag.CompensateFire(0, state.Entities[0].Id, reportedView, reportedView);

            Assert.Equal(LagCompensator.Outcome.DegradedFire, outcome);
            Assert.Equal(0L, lag.CompensatedCount);
        }

        /// <summary>
        /// **回溯补判必须带历史瞄准点**（AimPoint 单口径，《固定斜视角射击方案专项设计》§4）：
        /// 历史输入带头部带瞄准点 ⇒ 回溯判定命中头部带 ⇒ 产出 <see cref="FrameEventKind.Crit"/>，
        /// 且 <see cref="LagCompensator.LastHit"/> 为真（爆头走 Crit，<c>HasHit</c> 只认 Hit 会漏计）。
        /// 反例锁定：补判漏拷 AimPoint ⇒ 无点无效开火 ⇒ 无 Crit、<c>LastHit=false</c>。
        /// </summary>
        [Fact]
        public void 回溯补判带历史瞄准点_远距命中头部带_爆头成立且计为命中()
        {
            var map = BuildMap();
            var state = new SimWorldState { RngState = 5UL };
            state.Spawn(new EntitySlot { Hp = CombatConfig.EntityHp, Pos = map.SpawnPoints[0] }, out _);
            state.Spawn(new EntitySlot { Hp = CombatConfig.EntityHp, Pos = map.SpawnPoints[1] }, out _);
            var ring = new SnapshotRing(SimConfig.LagCompHistory);
            var lag = new LagCompensator(state, map, PlayerCount, ring, CombatValues.Default, WeaponTable.Default);

            // 全程不动（MoveX=0）——本例只验瞄准点口径，不掺移动；点取目标正上方头部带（20, 1.8）。
            for (int i = 0; i < 6; i++) StepWorld(state, map, ring, lag, 0f, 0f, shooterAimY: HeadAimY, shooterAimX: 20f);

            int serverFrame = state.Frame;
            int target = serverFrame - 3;                                   // 回溯 3 帧前（窗口 16 内）
            LagCompensator.Outcome outcome = lag.CompensateFire(0, state.Entities[0].Id,
                target + SimConfig.InterpFrames, target);

            Assert.Equal(LagCompensator.Outcome.Compensated, outcome);
            Assert.Equal(target, lag.LastTargetFrame);
            Assert.Equal(serverFrame, state.Frame);                          // 回溯不改现在

            Assert.True(HasEvent(state, FrameEventKind.Crit),
                $"历史瞄准点应参与回溯判定并命中头部带（事件 Kind 见断言消息）");
            Assert.True(lag.LastHit, "**Crit 也必须计入命中**（HasHit 漏 Crit 会让爆头不计命中）");

            // 爆头位级痕迹：伤害 = 基础 ×2（移位后必为偶数区间）
            bool critDamage = false;
            for (int i = 0; i < state.Cmds.Count; i++)
                if (state.Cmds.Items[i].Kind == SimCommandKind.Damage
                    && state.Cmds.Items[i].Target == state.Entities[1].Id)
                    critDamage = state.Cmds.Items[i].Amount % (1 << CombatConfig.HeadshotDamageShift) == 0;
            Assert.True(critDamage, "回溯爆头应产出移位后的伤害值");
        }

        /// <summary>
        /// **退化路径必须用当前帧瞄准点**（同口径的另一半）：窗口外/关补偿时判定虽按当前帧，
        /// 但瞄准点仍取当前帧输入——否则窗口外的玩家恒无点无效开火，永远打不出爆头，
        /// 且与同帧客户端预测不一致。
        /// </summary>
        [Fact]
        public void 窗口外退化_用当前帧瞄准点_爆头成立()
        {
            var map = BuildMap();
            var state = new SimWorldState { RngState = 5UL };
            state.Spawn(new EntitySlot { Hp = CombatConfig.EntityHp, Pos = map.SpawnPoints[0] }, out _);
            state.Spawn(new EntitySlot { Hp = CombatConfig.EntityHp, Pos = map.SpawnPoints[1] }, out _);
            var ring = new SnapshotRing(SimConfig.LagCompHistory);
            var lag = new LagCompensator(state, map, PlayerCount, ring, CombatValues.Default, WeaponTable.Default);

            for (int i = 0; i < 3; i++) StepWorld(state, map, ring, lag, 0f, 0f, shooterAimY: HeadAimY, shooterAimX: 20f);

            // 视角帧远早于窗口下界 → clamp 后无历史 → 退化（但当前帧瞄准点可得）
            int reportedView = state.Frame - (SimConfig.LagCompHistory + 10);
            LagCompensator.Outcome outcome = lag.CompensateFire(0, state.Entities[0].Id, reportedView, reportedView);

            Assert.Equal(LagCompensator.Outcome.DegradedFire, outcome);
            Assert.Equal(0L, lag.CompensatedCount);
            Assert.True(HasEvent(state, FrameEventKind.Crit),
                "退化路径应取当前帧瞄准点参与判定（否则无点开火、永远不爆头）");
            Assert.True(lag.LastHit, "退化路径的 Crit 也计为命中");
        }

        [Fact]
        public void 失效射手_不判定()
        {
            var map = BuildMap();
            var state = new SimWorldState();
            state.Spawn(new EntitySlot { Hp = CombatConfig.EntityHp, Pos = map.SpawnPoints[0] }, out _);
            var ring = new SnapshotRing(SimConfig.LagCompHistory);
            var lag = new LagCompensator(state, map, PlayerCount, ring, CombatValues.Default, WeaponTable.Default);

            LagCompensator.Outcome outcome = lag.CompensateFire(0, entityId: 12345L, viewFrame: 0, clientAckSnapshot: 0);
            Assert.Equal(LagCompensator.Outcome.InvalidShooter, outcome);
        }

        [Fact]
        public void 回溯不推进帧_不消费权威随机数()
        {
            var map = BuildMap();
            var state = new SimWorldState { RngState = 99UL };
            state.Spawn(new EntitySlot { Hp = CombatConfig.EntityHp, Pos = map.SpawnPoints[0] }, out _);
            state.Spawn(new EntitySlot { Hp = CombatConfig.EntityHp, Pos = map.SpawnPoints[1] }, out _);
            var ring = new SnapshotRing(SimConfig.LagCompHistory);
            var lag = new LagCompensator(state, map, PlayerCount, ring, CombatValues.Default, WeaponTable.Default);

            for (int i = 0; i < 10; i++) StepWorld(state, map, ring, lag, 0f, 0f);

            int frameBefore = state.Frame;
            ulong rngBefore = state.RngState;

            lag.CompensateFire(0, state.Entities[0].Id, frameBefore - 3 + SimConfig.InterpFrames, frameBefore - 3);

            Assert.Equal(frameBefore, state.Frame);        // 帧号不动
            Assert.Equal(rngBefore, state.RngState);       // 随机数不被回溯判定消费（还原）
        }
    }
}
