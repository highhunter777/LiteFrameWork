using System;
using System.Collections.Generic;
using LiteNet.Protocol;
using LiteSim;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 快照载波 / 差分器用例（《M10实施指导》§2.7 / §3 组）。
    /// 批② 的 FullSnapshotSource 占位已被批③ 的 <see cref="SnapshotDiffer"/> 取代（接口同形、循环零改动），
    /// 本文件覆盖：首帧全量 / 增量重建 == 权威态 / 静止增量趋零 / 活体变化转全量 / 全量兜底 / AOI / 分配器重建。
    /// </summary>
    public sealed class SnapshotSourceTests
    {
        private static SimWorldState BuildWorld(out SimMapData map)
        {
            map = new SimMapData { GroundY = 0f, HalfWidth = 50f, HalfDepth = 50f };
            map.SpawnPoints[0] = new SimVector3(-10f, 0f, 0f);
            map.SpawnPoints[1] = new SimVector3(10f, 0f, 0f);
            map.SpawnPointCount = 2;

            var s = new SimWorldState { RngState = 0x1234UL };
            s.Spawn(new EntitySlot { Hp = CombatConfig.EntityHp, Pos = map.SpawnPoints[0] }, out _);
            s.Spawn(new EntitySlot { Hp = CombatConfig.EntityHp, Pos = map.SpawnPoints[1] }, out _);
            return s;
        }

        private static SimWorldState Mirror() => new SimWorldState();

        private static readonly SimInputFrame[] NoInput = { new SimInputFrame(), new SimInputFrame() };

        /// <summary>构造一帧"某玩家移动"的输入（**必须带 EntityId** —— 缺省 0 会被 TryResolve 判为失效实体而丢弃，曾因此让用例空转）。</summary>
        private static SimInputFrame[] Move(SimWorldState state, int playerId, float moveX)
        {
            var inputs = new SimInputFrame[2];
            inputs[playerId] = new SimInputFrame { EntityId = state.Entities[playerId].Id, MoveX = moveX, AimX = 1f };
            return inputs;
        }

        [Fact]
        public void 首次构建_必为全量_且槽位数等于活体数()
        {
            var state = BuildWorld(out _);
            var differ = new SnapshotDiffer();

            Proto.StateSnapshot msg = differ.Build(state.Frame, state, ackInput: 0);

            Assert.True(msg.IsFull);
            Assert.Equal(state.AliveCount(), msg.Slots.Count);
            Assert.Equal(SimChecksum.ComputePublicChecksum(state), msg.Checksum);
        }

        [Fact]
        public void 增量重建_每帧套用后与权威checksum一致()
        {
            var state = BuildWorld(out SimMapData map);
            var differ = new SnapshotDiffer();
            var mirror = Mirror();
            var pump = new FramePump();

            // 镜像：从权威第 1 帧的全量快照起步，之后**只靠收到的快照** (Restore) 追赶；
            // 权威：用本轮真实输入继续演算。两边都以 1 帧/次的定次节拍推进（FramePump —— 网络对跑形态）
            pump.Step(1, state, map, Move(state, 0, 1f));
            SnapshotReassembler.Apply(differ.Build(state.Frame, state, 0), mirror, out _);

            var current = new SimInputFrame[2];
            foreach (float radius in new[] { 0f, SimConfig.AoiRadius })                  // AOI 关/开两种可见集各验一遍
            {
                differ = new SnapshotDiffer();                                           // 重置基线（换可见集）
                for (int i = 0; i < 8; i++)
                {
                    current[0] = default; current[1] = default;
                    current[i % 2] = new SimInputFrame { EntityId = state.Entities[i % 2].Id, MoveX = i % 3 == 0 ? -1f : 1f, AimX = 1f };
                    pump.Step(1, state, map, current);

                    Proto.StateSnapshot msg = differ.Build(state.Frame, state, 0,
                        state.Entities[0].Pos, radius, forceFull: false);
                    SnapshotReassembler.Apply(msg, mirror, out uint checksum);
                    Assert.Equal(SimChecksum.ComputePublicChecksum(state), checksum);   // 公共口径 checksum 随包下发
                    Assert.Equal(state.Frame, mirror.Frame);

                    // 镜像末态 == 权威末态：关闭 AOI 时**全部**槽位逐位对上；开启时可见集为子集，
                    // 已收到的可见槽位必须逐位准确（未收到的不比——那是 AOI 的裁剪语义）
                    Assert.Equal(state.AliveCount(), mirror.AliveCount());
                    for (int s = 0; s < SimConfig.MaxEntities; s++)
                    {
                        if (!state.IsAlive(s) || !mirror.IsAlive(s)) continue;
                        Assert.Equal(BitConverter.SingleToInt32Bits(state.Entities[s].Pos.X), BitConverter.SingleToInt32Bits(mirror.Entities[s].Pos.X));
                        Assert.Equal(BitConverter.SingleToInt32Bits(state.Entities[s].Pos.Z), BitConverter.SingleToInt32Bits(mirror.Entities[s].Pos.Z));
                        Assert.Equal(BitConverter.SingleToInt32Bits(state.Entities[s].Vel.X), BitConverter.SingleToInt32Bits(mirror.Entities[s].Vel.X));
                        Assert.Equal(BitConverter.SingleToInt32Bits(state.Entities[s].Yaw), BitConverter.SingleToInt32Bits(mirror.Entities[s].Yaw));
                        Assert.Equal(state.Entities[s].Hp, mirror.Entities[s].Hp);
                    }
                }
            }
        }

        [Fact]
        public void AOI裁剪时_未可见槽位不更新但可见槽位逐位准确()
        {
            var state = BuildWorld(out SimMapData map);
            var differ = new SnapshotDiffer();
            var mirror = Mirror();
            var pump = new FramePump();

            // 让 p1 远离 p0（AOI 半径 30m 之外），p0 留在原地
            for (int i = 0; i < 20; i++) pump.Step(1, state, map, Move(state, 1, 1f));

            // 再跑几帧：p1 继续远离（不可见，不发），p0 不动
            for (int i = 0; i < 5; i++) pump.Step(1, state, map, Move(state, 1, 1f));
            Proto.StateSnapshot msg = differ.Build(state.Frame, state, 0, state.Entities[0].Pos, SimConfig.AoiRadius, false);

            // 可见槽位（p0）必须准确；整体槽位数 < 活体数（p1 被裁）
            bool p0Visible = false;
            for (int i = 0; i < msg.Slots.Count; i++) if (msg.Slots[i].Slot == 0) p0Visible = true;
            Assert.True(p0Visible, "视点自身必须可见（不漏发）");
            Assert.True(msg.Slots.Count < state.AliveCount() || msg.IsFull,
                $"远离的实体应被 AOI 裁掉（slots={msg.Slots.Count} alive={state.AliveCount()} full={msg.IsFull}）");

            // 关闭 AOI 后两人都可见 → 槽位补齐
            Proto.StateSnapshot all = differ.Build(state.Frame, state, 0, state.Entities[0].Pos, 0f, true);
            Assert.Equal(state.AliveCount(), all.Slots.Count);
        }

        [Fact]
        public void 静止场景_增量趋零()
        {
            var state = BuildWorld(out SimMapData map);
            var differ = new SnapshotDiffer();
            var pump = new FramePump();

            differ.Build(state.Frame, state, 0);                  // 首帧全量（建立基线）

            for (int i = 0; i < 3; i++)                           // 重力到贴地静止（每步都广播，模拟真实的每帧广播）
            {
                pump.Step(1, state, map, NoInput);
                differ.Build(state.Frame, state, 0);
            }
            Assert.Equal(0, differ.LastDeltaCount);               // 完全静止：零槽位
            Assert.Equal(3, state.Frame);                         // 帧号连续推进（差分器不改帧号）

            var both = new SimInputFrame[2];                      // 两人反向移动 → 两个槽位都变化
            both[0] = new SimInputFrame { EntityId = state.Entities[0].Id, MoveX = 1f, AimX = 1f };
            both[1] = new SimInputFrame { EntityId = state.Entities[1].Id, MoveX = -1f, AimX = 1f };
            pump.Step(1, state, map, both);
            differ.Build(state.Frame, state, 0);
            Assert.Equal(2, differ.LastDeltaCount);
        }

        [Fact]
        public void 活体集合变化_自动转全量()
        {
            var state = BuildWorld(out SimMapData map);
            var differ = new SnapshotDiffer();
            var pump = new FramePump();
            differ.Build(state.Frame, state, 0);
            Assert.Equal(2, state.AliveCount());

            state.Despawn(state.Entities[1].Id);                  // 缺席 = 未变化 的语义无法表达"死了" → 必须全量
            pump.Step(1, state, map, NoInput);
            Proto.StateSnapshot msg = differ.Build(state.Frame, state, 0);
            Assert.True(msg.IsFull, "活体集合变化后必须转全量");
            Assert.Single(msg.Slots);

            state.Spawn(new EntitySlot { Hp = 50, Pos = new SimVector3(3f, 0f, 3f) }, out _);
            pump.Step(1, state, map, NoInput);
            msg = differ.Build(state.Frame, state, 0);
            Assert.True(msg.IsFull, "新生成实体后必须转全量");
            Assert.Equal(2, msg.Slots.Count);
        }

        [Fact]
        public void 全量兜底判据_ack落后与周期()
        {
            var state = BuildWorld(out _);
            var differ = new SnapshotDiffer();
            Proto.StateSnapshot msg = differ.Build(state.Frame, state, ackInput: 1);
            Assert.Equal(1, msg.AckInput);

            Assert.True(differ.NeedsFull(differ.LastBroadcastFrame - ProtocolConstants.FullResendAckLagFrames - 1),
                "ack 落后超阈值应触发全量");
            Assert.False(differ.NeedsFull(differ.LastBroadcastFrame - 1), "ack 紧跟不应触发全量");
        }

        [Fact]
        public void AOI_关闭全可见_开启按格裁剪且自己必在()
        {
            var state = BuildWorld(out _);
            state.Spawn(new EntitySlot { Hp = CombatConfig.EntityHp, Pos = new SimVector3(45f, 0f, 45f) }, out int farSlot);

            var visible = new List<int>();
            var aoi = new AoiFilter();
            SimVector3 viewer = state.Entities[0].Pos;

            aoi.CollectVisible(in state, viewer, radius: 0f, visible);              // 0 = 关闭 AOI（全图广播）
            Assert.Equal(state.AliveCount(), visible.Count);

            visible.Clear();
            aoi.CollectVisible(in state, viewer, radius: SimConfig.AoiRadius, visible);
            Assert.Contains(0, visible);                                           // 自己必在
            Assert.DoesNotContain(farSlot, visible);                               // 45m 外（半径 30m）不可见
            Assert.True(visible.Count < state.AliveCount());

            // 与全量的槽位集合是**包含关系**（验收口径：AOI 只裁剪广播，不改判定）
            Assert.Equal(state.AliveCount(), visible.Count + 1);
        }

        [Fact]
        public void 快照重建后_镜像分配器不与权威Id撞车()
        {
            var state = BuildWorld(out _);
            var differ = new SnapshotDiffer();
            var mirror = Mirror();

            SnapshotReassembler.Apply(differ.Build(state.Frame, state, 0), mirror, out _);

            long newId = mirror.Spawn(new EntitySlot { Hp = 10, Pos = new SimVector3(1f, 0f, 1f) }, out _);
            Assert.NotEqual(state.Entities[0].Id, newId);          // 版本表由 Id>>16 重建 → 不撞既有实体
            Assert.NotEqual(state.Entities[1].Id, newId);
        }

        [Fact]
        public void 私有面_只附本人_比赛状态层随每份快照()
        {
            var state = BuildWorld(out _);
            var differ = new SnapshotDiffer();

            // 两名玩家的私有运行态各不相同（弹药/技能 CD/局内包——只应出现在本人的快照里）
            state.Weapons[0 * SimConfig.WeaponSlotsPerEntity].MagAmmo = 30;
            state.Weapons[1 * SimConfig.WeaponSlotsPerEntity].MagAmmo = 8;
            state.Actions[1 * SimConfig.ActionSlotsPerEntity + 2].CooldownEnd = 600;      // B 的 Skill3
            state.MatchBag[1 * SimConfig.MatchBagSlotsPerEntity + 3].ItemDefId = 701;    // B 的背包
            state.Match = new MatchStateData { Phase = 1, Timer = 10800, Round = 1 };

            long idA = state.Entities[0].Id;
            long idB = state.Entities[1].Id;

            differ.BeginFrame(state.Frame, state, forceFull: false);   // 每广播帧一次（两段式）
            Proto.StateSnapshot forA = differ.BuildFor(state.Frame, state, 0, state.Entities[0].Pos, SimConfig.AoiRadius, idA);
            Proto.StateSnapshot forB = differ.BuildFor(state.Frame, state, 0, state.Entities[1].Pos, SimConfig.AoiRadius, idB);
            Proto.StateSnapshot bare = differ.BuildFor(state.Frame, state, 0, state.Entities[0].Pos, SimConfig.AoiRadius, 0L);

            // 比赛状态层：每份快照都带（room 级、全体同值）
            Assert.Equal(1, forA.Match.Phase);
            Assert.Equal(10800, forA.Match.Timer);
            Assert.Equal(1, forB.Match.Round);

            // 私有面：只附会话归属本人；不带他人的量（防泄漏，业务总设计 §1）
            Assert.NotNull(forA.PrivateState);
            Assert.Equal(idA, forA.PrivateState.EntityId);
            Assert.Equal(30, forA.PrivateState.Weapons[0].MagAmmo);
            Assert.Equal(0, forA.PrivateState.Skills[2].CooldownEnd);        // B 的 Skill3 CD 不在 A 的快照里
            Assert.Equal(0, forA.PrivateState.Bag[3].ItemDefId);             // B 的背包不在 A 的快照里

            Assert.NotNull(forB.PrivateState);
            Assert.Equal(idB, forB.PrivateState.EntityId);
            Assert.Equal(8, forB.PrivateState.Weapons[0].MagAmmo);
            Assert.Equal(600, forB.PrivateState.Skills[1].CooldownEnd);   // Skills[1] ↔ 动作槽 2（Skill2）
            Assert.Equal(701, forB.PrivateState.Bag[3].ItemDefId);

            Assert.Null(bare.PrivateState);                                  // 无会话归属（诊断/单测路径）不带私有面
        }
    }
}
