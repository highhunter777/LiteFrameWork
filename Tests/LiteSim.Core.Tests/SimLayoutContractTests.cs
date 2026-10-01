using System;
using System.Reflection;
using Xunit;

namespace LiteSim.Tests
{
    /// <summary>
    /// 第一批布局契约（《M8 实施指导》§2.1 验收 + §3"布局契约"组）：
    /// 用反射把"世界状态无引用类型字段"钉死——任何人往 SimWorldState 里加 List/class 成员，
    /// M9 的快照就会静默共享同一块内存（§7 风险 1），此测试当场暴露。
    /// 详尽的槽位/Id/确定性用例属第二批（§2.6），此处只放布局 + 常量对账 + 分配闭环冒烟。
    /// </summary>
    public class SimLayoutContractTests
    {
        private const BindingFlags AllInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [Fact]
        public void 布局_SimWorldState_无引用类型字段()
        {
            FieldInfo[] fields = typeof(SimWorldState).GetFields(AllInstance);
            Assert.True(fields.Length > 0);

            for (int i = 0; i < fields.Length; i++)
            {
                Type ft = fields[i].FieldType;
                Assert.True(ft.IsArray || ft.IsValueType,
                    fields[i].Name + " : " + ft.Name + " 违反布局契约（进快照的字段只允许值类型/数组，#5）");

                if (ft.IsArray)
                {
                    Assert.True(ft.GetElementType().IsValueType,
                        fields[i].Name + " 的数组元素必须是值类型（引用元素会被浅拷共享）");
                }
            }
        }

        [Fact]
        public void 布局_EntitySlot_仅值类型字段()
        {
            FieldInfo[] fields = typeof(EntitySlot).GetFields(AllInstance);
            Assert.True(fields.Length > 0);

            for (int i = 0; i < fields.Length; i++)
            {
                Assert.True(fields[i].FieldType.IsValueType,
                    fields[i].Name + " : " + fields[i].FieldType.Name + " 必须是值类型（blittable，#3）");
            }
        }

        [Fact]
        public void 开火窗_公共面_全量与公共口径都必变()
        {
            // 批次C：开火驻留窗是**改写 Vel 的判定输入**（瞄准 ∨ 开火态 → 限速）——必须进全量 checksum
            // （漏一个 = 重放对账漏检）。
            // 批次D 口径反转：开火窗**公共化**（fire_stance_frames=19）——客户端预测/回滚重放必须能从
            // 快照重建窗，缺失 ⇒ 窗内限速/朝向分叉 ⇒ 逐快照纠偏＝橡皮筋（实测 2026-10-01，"不开火也移不动"）。
            // ⇒ 公共口径**必含**：窗变 ⇒ 公共 checksum 必变（差分基线/和解锚点同步覆盖——防漏发静默分叉）。
            // 批C+：离场转向标记（FaceExitTurning）保持**私有面**——可由窗+输入在重放中重推导，
            // 不占协议字段号（1 帧边界误差可接受）。
            var world = new SimWorldState { RngState = 1UL };
            world.Spawn(new EntitySlot { Hp = 100 }, out int slot);

            uint full0 = SimChecksum.ComputeStateChecksum(world);
            uint public0 = SimChecksum.ComputePublicChecksum(world);

            world.Entities[slot].FireStanceFrames = (byte)CombatConfig.FireStanceFrames;

            Assert.True(full0 != SimChecksum.ComputeStateChecksum(world),
                "开火窗进全量口径（判定输入——重放对账必含）");
            Assert.True(public0 != SimChecksum.ComputePublicChecksum(world),
                "开火窗进公共口径（批次D 公共化——差分基线/和解锚点/回滚基线必须携带）");

            // 回滚重建面直证（LiteNet 侧 codec 往返留 SnapshotCodecTests——本工程不引用 LiteNet）：
            // 快照摘要捕获侧就在本工程（SimWorldStateSnapshot）
            var snap = new SimWorldStateSnapshot();
            Assert.True(world.TryResolve(world.Entities[slot].Id, out int slot2));
            snap.CaptureFull(world);
            Assert.Equal((byte)CombatConfig.FireStanceFrames, snap.Entities[slot2].FireStanceFrames);

            uint full1 = SimChecksum.ComputeStateChecksum(world);
            uint public1 = SimChecksum.ComputePublicChecksum(world);

            world.Entities[slot].FaceExitTurning = 1;

            Assert.True(full1 != SimChecksum.ComputeStateChecksum(world),
                "离场转向标记进全量口径（改写 Yaw 的过渡状态——重放对账必含）");
            Assert.True(public1 == SimChecksum.ComputePublicChecksum(world),
                "离场转向标记不进公共口径（可由窗+输入重推导——不占协议字段号）");

            // 窗长/转向速率不再数字钉：窗长是**用户实时调参项**（单源 CombatConfig.FireStanceFrames，
            // 历史曾 60/90 往返）；跨端一致性由 buildHash（源码）+ digest（联机身份）守卫——那才是系统级闸门
            Assert.True(CombatConfig.FireStanceFrames > 0 && CombatConfig.FaceTurnRadPerSec > 0f);
        }

        [Fact]
        public void 布局_SimConfig_常量与开工清单对账()
        {
            // 《状态同步实施方案》§11-3：60Hz / inputDelay 1 / MaxCatchUp 5 / MaxRollbackFrames 8 / MaxEntities 256
            Assert.Equal(60, SimConfig.TickRate);
            Assert.Equal(1, SimConfig.InputDelay);
            Assert.Equal(5, SimConfig.MaxCatchUp);
            Assert.Equal(8, SimConfig.MaxRollbackFrames);
            Assert.Equal(256, SimConfig.MaxEntities);
            Assert.Equal(SimMath.Dt, SimConfig.Dt);
            Assert.Equal(32, SimConfig.CustomBytesPerEntity);
            Assert.Equal(256, SimConfig.GlobalsBytes);
            // 《游戏业务系统总设计》§3.1 固定布局容量（P0 契约冻结）
            Assert.Equal(2, SimConfig.WeaponSlotsPerEntity);
            Assert.Equal(4, SimConfig.ActionSlotsPerEntity);
            Assert.Equal(4, SimConfig.StatusSlotsPerEntity);
            Assert.Equal(12, SimConfig.MatchBagSlotsPerEntity);
        }

        [Fact]
        public void 布局_P0运行态结构_仅值类型字段()
        {
            // P0 新增运行态必须与 EntitySlot 同纪律（blittable）：进快照/校验的前提
            AssertValueTypesOnly(typeof(WeaponRuntime));
            AssertValueTypesOnly(typeof(ActionRuntime));
            AssertValueTypesOnly(typeof(StatusSlotData));
            AssertValueTypesOnly(typeof(MatchBagSlot));
            AssertValueTypesOnly(typeof(MatchStateData));
        }

        private static void AssertValueTypesOnly(Type t)
        {
            FieldInfo[] fields = t.GetFields(AllInstance);
            Assert.True(fields.Length > 0, t.Name + " 无字段");

            for (int i = 0; i < fields.Length; i++)
            {
                Assert.True(fields[i].FieldType.IsValueType,
                    t.Name + "." + fields[i].Name + " : " + fields[i].FieldType.Name + " 必须是值类型（blittable）");
            }
        }

        [Fact]
        public void 槽位_Spawn解析释放闭环_冒烟()
        {
            var s = new SimWorldState();

            var proto = new EntitySlot { Hp = CombatConfig.EntityHp, Pos = new SimVector3(1f, 0f, 2f) };
            long id = s.Spawn(in proto, out int slotIndex);
            Assert.True(id > 0L, "首个分配 version=1，Id 必为正");
            Assert.True(slotIndex >= 0);
            Assert.Equal(1, s.AliveCount());

            Assert.True(s.TryResolve(id, out int resolved));
            Assert.Equal(slotIndex, resolved);
            Assert.Equal(100, s.Entities[resolved].Hp);

            s.Despawn(id);
            Assert.False(s.TryResolve(id, out int gone));
            Assert.Equal(0, s.AliveCount());

            // Double-free 静默忽略（§3 用例）
            s.Despawn(id);
            Assert.Equal(0, s.AliveCount());
        }

        [Fact]
        public void 槽位_同槽复用_Id不复用_冒烟()
        {
            var s = new SimWorldState();

            // 灌满 256 槽 → 游标环形归零；世界满时分配失败（Id=0，slotIndex=-1）
            long[] ids = new long[SimConfig.MaxEntities];
            for (int i = 0; i < SimConfig.MaxEntities; i++)
            {
                ids[i] = s.Spawn(default(EntitySlot), out int _);
                Assert.True(ids[i] > 0L);
            }
            Assert.Equal(0L, s.Spawn(default(EntitySlot), out int full));
            Assert.Equal(-1, full);

            s.Despawn(ids[0]);                               // 回收槽 0
            long second = s.Spawn(default(EntitySlot), out int slotB);
            Assert.Equal(0, slotB);                          // 游标已绕回，回收到同一槽
            Assert.NotEqual(ids[0], second);                  // version 递增 → Id 必不同（#7）
            Assert.False(s.TryResolve(ids[0], out int stale)); // 旧 Id 失效（跨帧引用唯一入口）
        }

        [Fact]
        public void 快照_CopyTo逐数组深拷_改一处互不影响()
        {
            var src = new SimWorldState();
            var dst = new SimWorldState();

            long id = src.Spawn(new EntitySlot { Hp = 50, Pos = new SimVector3(3f, 0f, 4f) }, out int slot);
            src.Frame = 123;
            src.Globals[7] = 0xAB;
            src.CustomData[slot * SimConfig.CustomBytesPerEntity + 3] = 0xCD;
            src.Match = new MatchStateData { Phase = 1, Timer = 10800, Round = 1, Winner = id };
            src.Resources[slot] = 77;
            src.Weapons[slot * SimConfig.WeaponSlotsPerEntity + 1] = new WeaponRuntime
            {
                WeaponDefId = 9002, MagAmmo = 8, ReserveAmmo = 32,
                NextFireFrame = 45, ReloadEndFrame = 0, EquipEndFrame = 12, State = WeaponSlotState.Ready, ShotSeq = 3,
            };
            src.Actions[slot * SimConfig.ActionSlotsPerEntity + 0] = new ActionRuntime
            {
                ActionId = 301, StartFrame = 120, Phase = ActionPhase.Windup, CastToken = 9, CooldownEnd = 480, Charges = 1,
            };
            src.Actions[slot * SimConfig.ActionSlotsPerEntity + 2] = new ActionRuntime { ActionId = 305, CooldownEnd = 300, Charges = 2 };
            src.Status[slot * SimConfig.StatusSlotsPerEntity + 0] = new StatusSlotData { EffectId = 51, EndFrame = 900, Param = 60 };
            src.MatchBag[slot * SimConfig.MatchBagSlotsPerEntity + 11] = new MatchBagSlot { ItemDefId = 701, Count = 3, QuickSlot = 4 };

            src.CopyTo(dst);
            Assert.Equal(123, dst.Frame);
            Assert.Equal((byte)0xAB, dst.Globals[7]);
            Assert.Equal((byte)0xCD, dst.CustomData[slot * SimConfig.CustomBytesPerEntity + 3]);
            Assert.True(dst.TryResolve(id, out int resolved));
            Assert.Equal(50, dst.Entities[resolved].Hp);
            Assert.Equal(1, dst.Match.Phase);
            Assert.Equal(10800, dst.Match.Timer);
            Assert.Equal(id, dst.Match.Winner);
            Assert.Equal(77, dst.Resources[resolved]);
            Assert.Equal(9002, dst.Weapons[resolved * SimConfig.WeaponSlotsPerEntity + 1].WeaponDefId);
            Assert.Equal(8, dst.Weapons[resolved * SimConfig.WeaponSlotsPerEntity + 1].MagAmmo);
            Assert.Equal(WeaponSlotState.Ready, dst.Weapons[resolved * SimConfig.WeaponSlotsPerEntity + 1].State);
            Assert.Equal(301, dst.Actions[resolved * SimConfig.ActionSlotsPerEntity + 0].ActionId);
            Assert.Equal(ActionPhase.Windup, dst.Actions[resolved * SimConfig.ActionSlotsPerEntity + 0].Phase);
            Assert.Equal(305, dst.Actions[resolved * SimConfig.ActionSlotsPerEntity + 2].ActionId);
            Assert.Equal(51, dst.Status[resolved * SimConfig.StatusSlotsPerEntity + 0].EffectId);
            Assert.Equal(701, dst.MatchBag[resolved * SimConfig.MatchBagSlotsPerEntity + 11].ItemDefId);
            Assert.Equal(4, dst.MatchBag[resolved * SimConfig.MatchBagSlotsPerEntity + 11].QuickSlot);

            // 深拷独立性：改 dst 一字节，src 必须不受影响（§7 风险 1 对策）
            dst.Globals[7] = 0x11;
            dst.Entities[resolved].Hp = 99;
            dst.Resources[resolved] = 1;
            dst.Match.Phase = 2;
            dst.Weapons[resolved * SimConfig.WeaponSlotsPerEntity + 1].MagAmmo = 0;
            Assert.Equal((byte)0xAB, src.Globals[7]);
            Assert.Equal(50, src.Entities[slot].Hp);
            Assert.Equal(77, src.Resources[slot]);
            Assert.Equal(1, src.Match.Phase);
            Assert.Equal(8, src.Weapons[slot * SimConfig.WeaponSlotsPerEntity + 1].MagAmmo);

            // 瞬态缓冲不随快照走（§3.7/决策⑥）
            src.Cmds.Write(SimCommandKind.Damage, id, 0L, 10);
            Assert.Equal(1, src.Cmds.Count);
            src.CopyTo(dst);
            Assert.Equal(0, dst.Cmds.Count);
        }

        [Fact]
        public void 槽位_Despawn清零每实体运行态_空槽校验值恒定()
        {
            var a = new SimWorldState();
            long id = a.Spawn(new EntitySlot { Hp = 50 }, out int slot);
            a.Entities[slot].Kills = 7;
            a.Resources[slot] = 88;
            a.Weapons[slot * SimConfig.WeaponSlotsPerEntity] = new WeaponRuntime { WeaponDefId = 1, MagAmmo = 30 };
            a.Actions[slot * SimConfig.ActionSlotsPerEntity] = new ActionRuntime { ActionId = 2, CooldownEnd = 99 };
            a.Status[slot * SimConfig.StatusSlotsPerEntity] = new StatusSlotData { EffectId = 3, Param = 60 };
            a.MatchBag[slot * SimConfig.MatchBagSlotsPerEntity] = new MatchBagSlot { ItemDefId = 4, Count = 5 };

            a.Despawn(id);

            // 上一占用者的残留必须清干净：空槽校验值恒定 = 同种子重放可对账的前提（§3.6）
            Assert.Equal(0, a.Resources[slot]);
            Assert.Equal(0, a.Weapons[slot * SimConfig.WeaponSlotsPerEntity].MagAmmo);
            Assert.Equal(0, a.Weapons[slot * SimConfig.WeaponSlotsPerEntity].WeaponDefId);
            Assert.Equal(0, a.Actions[slot * SimConfig.ActionSlotsPerEntity].ActionId);
            Assert.Equal(0, a.Status[slot * SimConfig.StatusSlotsPerEntity].EffectId);
            Assert.Equal(0, a.MatchBag[slot * SimConfig.MatchBagSlotsPerEntity].ItemDefId);

            var fresh = new SimWorldState();
            Assert.Equal(SimChecksum.ComputeStateChecksum(fresh), SimChecksum.ComputeStateChecksum(a));
        }

        [Fact]
        public void 校验口径_全量覆盖P0运行态_公共口径只认公共面()
        {
            var s = new SimWorldState();
            long id = s.Spawn(new EntitySlot { Hp = 50 }, out int slot);
            uint full0 = SimChecksum.ComputeChecksum(s);
            uint public0 = SimChecksum.ComputePublicChecksum(s);

            // 私有面变化：全量口径必变，公共口径不变（客户端重建不了私有面——和解锚点不得包含）
            s.Weapons[slot * SimConfig.WeaponSlotsPerEntity].MagAmmo = 30;
            s.Actions[slot * SimConfig.ActionSlotsPerEntity + 2].CooldownEnd = 600;
            s.Status[slot * SimConfig.StatusSlotsPerEntity].Param = 60;
            s.MatchBag[slot * SimConfig.MatchBagSlotsPerEntity].ItemDefId = 9;
            s.Resources[slot] = 5;
            s.RngState = 123UL;
            Assert.NotEqual(full0, SimChecksum.ComputeChecksum(s));
            Assert.Equal(public0, SimChecksum.ComputePublicChecksum(s));

            // 公共面变化：两种口径都必变
            s.Entities[slot].Shield = 60;
            s.Entities[slot].Kills = 1;
            s.Entities[slot].SelectedWeapon = 1;
            s.Actions[slot * SimConfig.ActionSlotsPerEntity].ActionId = 301;
            s.Actions[slot * SimConfig.ActionSlotsPerEntity].Phase = ActionPhase.Active;
            s.Match.Timer = 10800;
            Assert.NotEqual(public0, SimChecksum.ComputePublicChecksum(s));
            Assert.NotEqual(full0, SimChecksum.ComputeChecksum(s));

            // 公共口径与全量口径是两个值域（防误替换成同一实现）
            Assert.NotEqual(SimChecksum.ComputeChecksum(s), SimChecksum.ComputePublicChecksum(s));
        }
    }
}
