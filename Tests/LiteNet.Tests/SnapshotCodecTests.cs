using System;
using LiteNet.Protocol;
using LiteSim;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>快照编解码用例（《M10 实施指导》§3：SlotDelta 位级一致 / 全量快照对账；
    /// P0 扩：公共战斗面 + 主动作摘要往返、比赛状态层、本人私有面打包/应用单源）。</summary>
    public class SnapshotCodecTests
    {
        private static SimWorldState BuildWorld()
        {
            var s = new SimWorldState { RngState = 0x5EEDBEEF12345678UL };
            s.Spawn(new EntitySlot { Hp = CombatConfig.EntityHp, Pos = new SimVector3(0f, 0f, 0f), Yaw = 0.5f }, out int slot0);
            s.Spawn(new EntitySlot { Hp = 75, Pos = new SimVector3(10f, 0f, -3.25f), Vel = new SimVector3(-1.5f, 0f, 2.25f), Yaw = 3.1f, Flags = 2u }, out int slot1);

            // P0 公共战斗面 + 主动作摘要（差分/校验/往返三处共用同一批字段——用例必须覆盖到位）
            s.Entities[slot0].Shield = 60;
            s.Entities[slot0].Kills = 3;
            s.Entities[slot0].Deaths = 1;
            s.Entities[slot0].SelectedWeapon = 1;
            s.Entities[slot1].FireStanceFrames = (byte)CombatConfig.FireStanceFrames;   // 批次D：开火驻留窗往返覆盖（公共面——回滚基线重建面）
            s.Actions[slot0 * SimConfig.ActionSlotsPerEntity] = new ActionRuntime
            {
                ActionId = 301, StartFrame = 120, Phase = ActionPhase.Active, CastToken = 7,
            };
            return s;
        }

        [Fact]
        public void SlotDelta_活体槽位往返位级一致_含公共战斗面与主动作摘要()
        {
            var s = BuildWorld();

            for (int i = 0; i < SimConfig.MaxEntities; i++)
            {
                if (!s.IsAlive(i)) continue;
                ref EntitySlot e = ref s.Entities[i];
                ref ActionRuntime active = ref s.Actions[i * SimConfig.ActionSlotsPerEntity];

                Proto.SlotDelta wire = SnapshotCodec.ToDelta(i, in e, in active);
                SnapshotCodec.FromDelta(wire, out int slot, out EntitySlot back);

                Assert.Equal(i, slot);
                Assert.Equal(e.Id, back.Id);
                Assert.Equal(BitConverter.SingleToInt32Bits(e.Pos.X), BitConverter.SingleToInt32Bits(back.Pos.X));
                Assert.Equal(BitConverter.SingleToInt32Bits(e.Pos.Y), BitConverter.SingleToInt32Bits(back.Pos.Y));
                Assert.Equal(BitConverter.SingleToInt32Bits(e.Pos.Z), BitConverter.SingleToInt32Bits(back.Pos.Z));
                Assert.Equal(BitConverter.SingleToInt32Bits(e.Vel.X), BitConverter.SingleToInt32Bits(back.Vel.X));
                Assert.Equal(BitConverter.SingleToInt32Bits(e.Vel.Y), BitConverter.SingleToInt32Bits(back.Vel.Y));
                Assert.Equal(BitConverter.SingleToInt32Bits(e.Vel.Z), BitConverter.SingleToInt32Bits(back.Vel.Z));
                Assert.Equal(BitConverter.SingleToInt32Bits(e.Yaw), BitConverter.SingleToInt32Bits(back.Yaw));
                Assert.Equal(e.Hp, back.Hp);
                Assert.Equal(e.Flags, back.Flags);
                Assert.Equal(e.Shield, back.Shield);
                Assert.Equal(e.Kills, back.Kills);
                Assert.Equal(e.Deaths, back.Deaths);
                Assert.Equal(e.SelectedWeapon, back.SelectedWeapon);
                Assert.Equal(e.FireStanceFrames, back.FireStanceFrames);   // 批次D：开火驻留窗（公共面往返）

                // 主动作摘要（公共面——技能槽冷却/充能不在此，那是私有面）
                ActionRuntime action = SnapshotCodec.ActiveActionFromDelta(wire);
                Assert.Equal(active.ActionId, action.ActionId);
                Assert.Equal(active.StartFrame, action.StartFrame);
                Assert.Equal(active.Phase, action.Phase);
                Assert.Equal(0, action.CastToken);          // 摘要不含私有令牌
                Assert.Equal(0, action.CooldownEnd);
            }
        }

        [Fact]
        public void 全量快照_活体数与公共口径checksum与比赛状态对账()
        {
            var s = BuildWorld();
            s.Spawn(new EntitySlot { Hp = 1, Pos = new SimVector3(20f, 0f, 10f) }, out _); // 第 3 个活体
            s.Match = new MatchStateData { Phase = 1, Team = 0, Score = 0, Timer = 10800, Round = 1, Winner = 0 };

            Proto.StateSnapshot msg = SnapshotCodec.PackFull(frame: 42, s: s, ackInput: 40);

            Assert.True(msg.IsFull);
            Assert.Equal(42, msg.Frame);
            Assert.Equal(40, msg.AckInput);
            Assert.Equal(s.AliveCount(), msg.Slots.Count);
            Assert.Equal(SimChecksum.ComputePublicChecksum(s), msg.Checksum);   // 和解判定的位级锚点（公共口径）
            Assert.Null(msg.PrivateState);                                     // 私有面按会话另附——全量包不带

            // 比赛状态层随全量快照
            Assert.NotNull(msg.Match);
            Assert.Equal(1, msg.Match.Phase);
            Assert.Equal(10800, msg.Match.Timer);
            Assert.Equal(1, msg.Match.Round);
        }

        [Fact]
        public void 私有面_打包与应用单源往返()
        {
            var s = BuildWorld();
            long id = s.Entities[0].Id;
            int slot = 0;

            s.Resources[slot] = 42;
            s.Weapons[slot * SimConfig.WeaponSlotsPerEntity + 0] = new WeaponRuntime
            {
                WeaponDefId = 9001, MagAmmo = 30, ReserveAmmo = 90, NextFireFrame = 500,
                ReloadEndFrame = 0, EquipEndFrame = 12, State = WeaponSlotState.Ready, ShotSeq = 3,
            };
            s.Weapons[slot * SimConfig.WeaponSlotsPerEntity + 1] = new WeaponRuntime
            {
                WeaponDefId = 9002, MagAmmo = 8, ReserveAmmo = 32, State = WeaponSlotState.Reloading, ReloadEndFrame = 480,
            };
            s.Actions[slot * SimConfig.ActionSlotsPerEntity + 1] = new ActionRuntime { ActionId = 301, CooldownEnd = 600, Charges = 1 };
            s.Actions[slot * SimConfig.ActionSlotsPerEntity + 3] = new ActionRuntime { ActionId = 305, CooldownEnd = 180, Charges = 2 };
            s.Status[slot * SimConfig.StatusSlotsPerEntity + 0] = new StatusSlotData { EffectId = 51, EndFrame = 900, Param = 60 };
            s.MatchBag[slot * SimConfig.MatchBagSlotsPerEntity + 11] = new MatchBagSlot { ItemDefId = 701, Count = 3, QuickSlot = 4 };

            Proto.PrivateStateDelta wire = SnapshotCodec.PackPrivate(s, id);
            Assert.Equal(id, wire.EntityId);
            Assert.Equal(42, wire.Resource);
            Assert.Equal(SimConfig.WeaponSlotsPerEntity, wire.Weapons.Count);
            Assert.Equal(SimConfig.ActionSlotsPerEntity - 1, wire.Skills.Count);   // Skill1..3（主动作槽走公共面）
            Assert.Equal(SimConfig.StatusSlotsPerEntity, wire.Status.Count);
            Assert.Equal(SimConfig.MatchBagSlotsPerEntity, wire.Bag.Count);

            var dst = new SimWorldState();
            dst.Entities[0] = new EntitySlot { Id = id };                       // 槽位活体（应用需解析归属）
            dst.AliveBitmap[0] = 1u;
            dst.SetAllocatorIdleSlot(0);                                        // 版本表由 Id>>16 反推——否则 TryResolve 必失败
            SnapshotCodec.ApplyPrivate(wire, dst);

            Assert.Equal(42, dst.Resources[0]);
            Assert.Equal(9001, dst.Weapons[0].WeaponDefId);
            Assert.Equal(30, dst.Weapons[0].MagAmmo);
            Assert.Equal(WeaponSlotState.Ready, dst.Weapons[0].State);
            Assert.Equal(9002, dst.Weapons[1].WeaponDefId);
            Assert.Equal(WeaponSlotState.Reloading, dst.Weapons[1].State);
            Assert.Equal(480, dst.Weapons[1].ReloadEndFrame);
            Assert.Equal(301, dst.Actions[0 * SimConfig.ActionSlotsPerEntity + 1].ActionId);
            Assert.Equal(600, dst.Actions[0 * SimConfig.ActionSlotsPerEntity + 1].CooldownEnd);
            Assert.Equal(305, dst.Actions[0 * SimConfig.ActionSlotsPerEntity + 3].ActionId);
            Assert.Equal(51, dst.Status[0].EffectId);
            Assert.Equal(60, dst.Status[0].Param);
            Assert.Equal(701, dst.MatchBag[11].ItemDefId);
            Assert.Equal(4, dst.MatchBag[11].QuickSlot);
        }

        [Fact]
        public void 私有面_实体已死打包为null_应用静默忽略()
        {
            var s = BuildWorld();
            Assert.Null(SnapshotCodec.PackPrivate(s, entityId: 0L));             // Id 0 = 无效实体
            Assert.Null(SnapshotCodec.PackPrivate(s, entityId: 12345L));        // 不存在的实体

            // 应用侧：EntityId 解析失败的包不抛、不写（本人已死：下份公共快照即权威事实）
            var dst = new SimWorldState();
            var wire = new Proto.PrivateStateDelta { EntityId = 12345L, Resource = 9 };
            SnapshotCodec.ApplyPrivate(wire, dst);
            Assert.Equal(0, dst.Resources[0]);
        }
    }
}
