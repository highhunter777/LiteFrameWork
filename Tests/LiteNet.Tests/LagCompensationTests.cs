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

        /// <summary>把"两人都在动"的输入喂一步（EntityId 必带——缺省 0 会被判失效实体而整帧不生效）。</summary>
        private static SimInputFrame[] StepWorld(SimWorldState s, SimMapData map, SnapshotRing ring, LagCompensator lag,
            float shooterMoveX, float targetMoveX)
        {
            var inputs = new SimInputFrame[PlayerCount];
            inputs[0] = new SimInputFrame { EntityId = s.Entities[0].Id, MoveX = shooterMoveX, AimX = 1f };
            inputs[1] = new SimInputFrame { EntityId = s.Entities[1].Id, MoveX = targetMoveX, AimX = 1f };
            SimStep.Step(s, map, inputs);
            ring.Capture(s.Frame, s);
            lag.RecordInputs(s.Frame, inputs);
            return inputs;
        }

        [Fact]
        public void 高延迟开火_命中客户端所见位置_而非目标当前位置()
        {
            var map = BuildMap();
            var state = new SimWorldState { RngState = 7UL };
            state.Spawn(new EntitySlot { Hp = CombatConfig.EntityHp, Pos = map.SpawnPoints[0] }, out _);
            state.Spawn(new EntitySlot { Hp = CombatConfig.EntityHp, Pos = map.SpawnPoints[1] }, out _);
            var ring = new SnapshotRing(SimConfig.LagCompHistory);
            var lag = new LagCompensator(state, PlayerCount, ring);

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
            var lag = new LagCompensator(state, PlayerCount, ring);

            for (int i = 0; i < 3; i++) StepWorld(state, map, ring, lag, 0f, 1f);

            // 上报的视角帧远早于环窗口（窗口 16 帧）→ clamp 到窗口下界后仍无历史 → 退化
            int reportedView = state.Frame - (SimConfig.LagCompHistory + 10);   // 远早于窗口下界（clamp 后仍无历史）
            LagCompensator.Outcome outcome = lag.CompensateFire(0, state.Entities[0].Id, reportedView, reportedView);

            Assert.Equal(LagCompensator.Outcome.DegradedFire, outcome);
            Assert.Equal(0L, lag.CompensatedCount);
        }

        [Fact]
        public void 失效射手_不判定()
        {
            var map = BuildMap();
            var state = new SimWorldState();
            state.Spawn(new EntitySlot { Hp = CombatConfig.EntityHp, Pos = map.SpawnPoints[0] }, out _);
            var ring = new SnapshotRing(SimConfig.LagCompHistory);
            var lag = new LagCompensator(state, PlayerCount, ring);

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
            var lag = new LagCompensator(state, PlayerCount, ring);

            for (int i = 0; i < 10; i++) StepWorld(state, map, ring, lag, 0f, 0f);

            int frameBefore = state.Frame;
            ulong rngBefore = state.RngState;

            lag.CompensateFire(0, state.Entities[0].Id, frameBefore - 3 + SimConfig.InterpFrames, frameBefore - 3);

            Assert.Equal(frameBefore, state.Frame);        // 帧号不动
            Assert.Equal(rngBefore, state.RngState);       // 随机数不被回溯判定消费（还原）
        }
    }
}
