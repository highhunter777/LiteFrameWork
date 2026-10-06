using System;
using System.Collections.Generic;
using System.Reflection;
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
            s.Entities[slot1].FireStanceFrames = (byte)CombatConfig.FireStanceFrames;   // 开火驻留窗往返覆盖（公共面——回滚基线重建面）
            s.Entities[slot1].CorpseFrames = (byte)CombatConfig.CorpseFrames;           // 尸体期往返覆盖（corpse_frames=20——
            // 公共面且**进公共口径 checksum**：wire 缺失 ⇒ 客户端重建恒 0 ⇒ 尸体期内每帧假和解）
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
                Assert.Equal(e.FireStanceFrames, back.FireStanceFrames);   // 开火驻留窗（公共面往返）
                Assert.Equal(e.CorpseFrames, back.CorpseFrames);           // 尸体期（公共面往返——公共口径 checksum 覆盖它）

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

        /// <summary>
        /// **公共面字段全覆盖的 wire 往返守卫**（反射驱动，不逐字段手写）。
        ///
        /// 上面那条用例是逐字段断言的——**加字段忘了同步 codec 时它不会红**
        /// （没有对应断言）。CorpseFrames 当初正是这样漏掉的：公共 checksum 覆盖它、
        /// <c>SlotDelta</c> 却没有该字段，客户端重建恒 0 ⇒ 尸体期内每帧假和解。
        ///
        /// 本用例遍历 <see cref="EntitySlot"/> 上所有标注为 Public 的字段，
        /// 逐个扰动后经 ToDelta→FromDelta 往返，断言值必须原样回来。
        /// 新增公共面字段若漏改 codec，这里立刻失败。
        /// </summary>
        [Fact]
        public void 公共面字段_逐字段wire往返_漏同步codec即红()
        {
            const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var checkedFields = new List<string>();

            foreach (FieldInfo f in typeof(EntitySlot).GetFields(Instance))
            {
                if (f.GetCustomAttribute<StateLayerAttribute>()?.Layer != StateLayer.Public) continue;
                if (!f.FieldType.IsValueType) continue;
                checkedFields.Add(f.Name);

                var world = new SimWorldState();
                world.Spawn(new EntitySlot
                {
                    Hp = 100, Pos = new SimVector3(1f, 2f, 3f), Vel = new SimVector3(0.5f, 0f, -0.5f),
                    Yaw = 0.25f, Flags = 3u, Shield = 10, Kills = 2, Deaths = 1,
                    SelectedWeapon = 1, FireStanceFrames = 30, CorpseFrames = 12,
                }, out int slot);

                // 扰动该字段（值必须非默认，否则往返"零到零"会假绿）
                object boxed = world.Entities[slot];
                f.SetValue(boxed, Nudged(f, f.GetValue(boxed)));
                world.Entities[slot] = (EntitySlot)boxed;

                ref EntitySlot e = ref world.Entities[slot];
                ref ActionRuntime active = ref world.Actions[slot * SimConfig.ActionSlotsPerEntity];
                Proto.SlotDelta wire = SnapshotCodec.ToDelta(slot, in e, in active);
                SnapshotCodec.FromDelta(wire, out _, out EntitySlot back);

                object before = f.GetValue(e);
                object after = f.GetValue(back);
                Assert.True(SameValue(f.FieldType, before, after),
                    $"公共面字段 {f.Name} 经 wire 往返后值变了（{before} -> {after}）——"
                    + "SlotDelta/ToDelta/FromDelta 漏了它，客户端重建会恒为默认值");
            }

            Assert.True(checkedFields.Count >= 12, $"公共面字段数异常（{checkedFields.Count}）——标注体系可能被破坏");
        }

        private static bool SameValue(Type t, object a, object b)
        {
            if (t.Name == "SimVector3")
            {
                var va = (SimVector3)a; var vb = (SimVector3)b;
                return Bits(va.X) == Bits(vb.X) && Bits(va.Y) == Bits(vb.Y) && Bits(va.Z) == Bits(vb.Z);
            }
            if (t == typeof(float)) return Bits((float)a) == Bits((float)b);
            return Equals(a, b);
        }

        private static int Bits(float v) => BitConverter.SingleToInt32Bits(v);

        private static object Nudged(FieldInfo f, object current)
        {
            switch (current)
            {
                case long v: return v + 1000L;
                case int v: return v + 1000;
                case uint v: return v + 1000u;
                case byte v: return (byte)(v + 7);
                case float v: return v + 1.25f;
                case SimVector3 v: return new SimVector3(v.X + 1f, v.Y + 1f, v.Z + 1f);
                default: throw new InvalidOperationException("未覆盖的字段类型：" + f.FieldType.Name);
            }
        }

        [Fact]
        public void 尸体期_经wire往返后公共口径checksum不变_否则尸体期内每帧假和解()
        {
            // 回归用例：corpse_frames 未进 wire 时，客户端 FromDelta 重建的 CorpseFrames 恒 0，
            // 而 ComputePublicChecksum 混入该字段 ⇒ 尸体期 180 帧内权威 checksum 与本地预测值
            // 结构性不等 ⇒ RollbackSim.OnAuthoritativeSnapshot 每帧判为不符 → 假和解 + 重放。
            var s = BuildWorld();
            s.Frame = 7;                                       // 公共口径 checksum 含 Frame——两端须同帧号
            int dead = 0;
            s.Entities[dead].Hp = 0;
            s.Entities[dead].CorpseFrames = (byte)CombatConfig.CorpseFrames;   // 死亡帧置满（DamageSystem 同点）

            Proto.StateSnapshot msg = SnapshotCodec.PackFull(frame: s.Frame, s: s, ackInput: 6);
            var mirror = new SimWorldState();
            SnapshotReassembler.Apply(msg, mirror, out uint authoritativeChecksum);

            // 镜像必须真带上尸体期（否则下面两条 checksum 等式会靠"两端都是 0"侥幸成立，
            // 测不出 wire 缺字段——这正是原缺陷的伪装形态）
            Assert.Equal((byte)CombatConfig.CorpseFrames, mirror.Entities[dead].CorpseFrames);
            Assert.True(mirror.IsAlive(dead));   // 尸体期未满：死者仍占槽位（期内零交互，期满才回收）

            // 和解锚点的真实判据：本地预测态与权威镜像的公共口径 checksum 必须相等
            Assert.Equal(SimChecksum.ComputePublicChecksum(s), authoritativeChecksum);
            Assert.Equal(SimChecksum.ComputePublicChecksum(mirror), authoritativeChecksum);
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
