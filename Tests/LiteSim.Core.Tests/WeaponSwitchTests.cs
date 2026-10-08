using LiteSim;
using Xunit;

namespace LiteSim.Tests
{
    /// <summary>
    /// 切枪状态机用例（《游戏业务系统总设计》§5 切枪）：ButtonSwitchWeapon 边沿 → 选中槽
    /// 公共面翻面 + 目标槽 Switching 锁定（EquipEndFrame 到帧解除）→ Ready；
    /// 首切满弹装备、弹药保持、换弹打断、拒绝面（同槽/切枪中/无定义行/死亡）。
    /// 装载面（tb_weapon.slot → 槽位默认映射）由 <see cref="WeaponTableTests"/>（LiteNet 侧）钉。
    /// </summary>
    public sealed class WeaponSwitchTests
    {
        private static readonly SimMapData NoObstacles = new SimMapData();

        /// <summary>双槽表：槽 0 = 步兵枪（快切 30 帧）、槽 1 = 霰弹（慢切 40 帧——区分切枪时长来源）。</summary>
        private static WeaponTable TwoSlotTable()
        {
            var t = new WeaponTable();
            t.SetRow(0, damage: 16, rpm: 600, magazineSize: 30, reserveAmmo: 90, reloadFrames: 132,
                range: 100f, spread: 0f, pellets: 1, switchFrames: 30, automatic: true);
            t.SetRow(1, damage: 8, rpm: 75, magazineSize: 8, reserveAmmo: 24, reloadFrames: 168,
                range: 30f, spread: 0f, pellets: 8, switchFrames: 40, automatic: false);
            t.SetSlotDefault(0, 0);
            t.SetSlotDefault(1, 1);
            return t;
        }

        private static (SimWorldState world, long id, int slot) Spawn()
        {
            var world = new SimWorldState { RngState = 1UL };
            long id = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f) }, out int slot);
            WeaponSystem.Run(world, new[] { new SimInputFrame { EntityId = id } }, TwoSlotTable());   // 懒装备槽 0
            return (world, id, slot);
        }

        private static void Switch(SimWorldState world, long id, int to, uint seq = 1u)
            => WeaponSystem.Run(world, new[]
            {
                new SimInputFrame
                {
                    EntityId = id, Buttons = SimInputFrame.ButtonSwitchWeapon,
                    SelectedWeaponSlot = to, ActionSeq = seq,
                },
            }, TwoSlotTable());

        private static void FireOnce(SimWorldState world, long id, int frame)
        {
            world.Frame = frame;
            WeaponSystem.Run(world, new[]
            {
                new SimInputFrame { EntityId = id, AimPointX = 50f, AimPointY = 1f, AimPointZ = 0f, Buttons = SimInputFrame.ButtonFire },
            }, TwoSlotTable());
            ShootingSystem.Run(world, NoObstacles, new[]
            {
                new SimInputFrame { EntityId = id, AimPointX = 50f, AimPointY = 1f, AimPointZ = 0f, Buttons = SimInputFrame.ButtonFire },
            }, CombatValues.Default, TwoSlotTable());
        }

        private static int FireEvents(SimWorldState s)
        {
            int n = 0;
            for (int i = 0; i < s.Events.Count; i++)
                if (s.Events.Items[i].Kind == FrameEventKind.Fire) n++;
            return n;
        }

        private static ref WeaponRuntime WeaponAt(SimWorldState s, int slot, int weaponSlot)
            => ref s.Weapons[slot * SimConfig.WeaponSlotsPerEntity + weaponSlot];

        [Fact]
        public void 切枪_选中槽公共面立即翻_目标槽Switching锁定_到帧Ready()
        {
            var (world, id, slot) = Spawn();
            world.Frame = 0;
            Switch(world, id, to: 1);

            Assert.Equal(1, world.Entities[slot].SelectedWeapon);              // 公共面立即翻（远端武器外观随快照）
            ref WeaponRuntime sg = ref WeaponAt(world, slot, 1);
            Assert.Equal(WeaponSlotState.Switching, sg.State);
            Assert.Equal(0 + 40, sg.EquipEndFrame);                            // 切枪时长 = 目标武器表值（40 帧）
            Assert.Equal(1, sg.WeaponDefId);                                   // 首切：即刻满弹装备（数据面）
            Assert.Equal(8, sg.MagAmmo);
            Assert.Equal(24, sg.ReserveAmmo);

            // 切枪期开火被武器门拦（Switching ≠ Ready——无 Fire 事件、不扣弹）
            FireOnce(world, id, frame: 1);
            Assert.Equal(0, FireEvents(world));
            Assert.Equal(8, sg.MagAmmo);

            // 到帧前 1 帧仍锁定；到帧解除
            world.Frame = 39;
            WeaponSystem.Run(world, new[] { new SimInputFrame { EntityId = id } }, TwoSlotTable());
            Assert.Equal(WeaponSlotState.Switching, sg.State);
            world.Frame = 40;
            WeaponSystem.Run(world, new[] { new SimInputFrame { EntityId = id } }, TwoSlotTable());
            Assert.Equal(WeaponSlotState.Ready, sg.State);
            Assert.Equal(0, sg.EquipEndFrame);
        }

        [Fact]
        public void 切枪_到帧后开火霰弹弹丸消耗一发_切回步枪弹药保持()
        {
            var (world, id, slot) = Spawn();
            Switch(world, id, to: 1);
            world.Frame = 40;
            WeaponSystem.Run(world, new[] { new SimInputFrame { EntityId = id } }, TwoSlotTable());

            FireOnce(world, id, frame: 40);                                    // 霰弹击发（8 弹丸共享一发弹药）
            Assert.Equal(1, FireEvents(world));
            ref WeaponRuntime sg = ref WeaponAt(world, slot, 1);
            Assert.Equal(7, sg.MagAmmo);
            Assert.Equal(1, sg.ShotSeq);

            Switch(world, id, to: 0, seq: 2u);                                 // 切回步枪
            world.Frame = 40 + 30;                                             // 步枪切枪 30 帧
            WeaponSystem.Run(world, new[] { new SimInputFrame { EntityId = id } }, TwoSlotTable());
            Assert.Equal(WeaponSlotState.Ready, WeaponAt(world, slot, 0).State);
            Assert.Equal(30, WeaponAt(world, slot, 0).MagAmmo);                // 步枪弹药保持（全程未开火——切枪不动弹药）

            Switch(world, id, to: 1, seq: 3u);                                 // 再切回霰弹——弹药保持延续
            world.Frame = 40 + 30 + 40;
            WeaponSystem.Run(world, new[] { new SimInputFrame { EntityId = id } }, TwoSlotTable());
            sg = ref WeaponAt(world, slot, 1);
            Assert.Equal(WeaponSlotState.Ready, sg.State);
            Assert.Equal(7, sg.MagAmmo);                                        // 只打过一发——切走切回不动弹药
            Assert.Equal(1, sg.ShotSeq);
        }

        [Fact]
        public void 切枪_同槽重选no操作_切枪中再切拒绝_死亡拒绝()
        {
            var (world, id, slot) = Spawn();

            Switch(world, id, to: 0);                                          // 同槽 = no-op
            Assert.Equal(0, world.Entities[slot].SelectedWeapon);
            Assert.Equal(WeaponSlotState.Ready, WeaponAt(world, slot, 0).State);

            Switch(world, id, to: 1);
            Switch(world, id, to: 0, seq: 2u);                                  // 举起窗内再切 = 拒绝（防连按刷窗）
            Assert.Equal(1, world.Entities[slot].SelectedWeapon);
            Assert.Equal(WeaponSlotState.Switching, WeaponAt(world, slot, 1).State);

            world.Entities[slot].Hp = 0;                                       // 死亡实体切枪拒绝（尸体不受操控）
            world.Frame = 100;
            Switch(world, id, to: 0, seq: 3u);
            Assert.Equal(1, world.Entities[slot].SelectedWeapon);
        }

        [Fact]
        public void 切枪_打断换弹_旧槽回Ready换弹窗清零()
        {
            var (world, id, slot) = Spawn();
            ref WeaponRuntime rifle = ref WeaponAt(world, slot, 0);
            rifle.MagAmmo = 10;                                                 // 半弹仓
            world.Frame = 50;
            WeaponSystem.Run(world, new[]
            {
                new SimInputFrame { EntityId = id, Buttons = SimInputFrame.ButtonReload, ActionSeq = 1u },
            }, TwoSlotTable());
            Assert.Equal(WeaponSlotState.Reloading, rifle.State);

            Switch(world, id, to: 1);                                          // 切枪打断换弹（"换弹中途取消"）
            Assert.Equal(WeaponSlotState.Ready, rifle.State);
            Assert.Equal(0, rifle.ReloadEndFrame);
            Assert.Equal(10, rifle.MagAmmo);                                    // 已装填的不回退——中止即保持原弹匣
            Assert.Equal(WeaponSlotState.Switching, WeaponAt(world, slot, 1).State);
        }

        [Fact]
        public void 切枪_目标槽无表默认定义拒绝_旧槽不动()
        {
            // 单槽表：槽 1 无默认行（旧表形态）——切枪请求被拒
            var t = TestWeapons.NoSpread();
            var world = new SimWorldState { RngState = 1UL };
            long id = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f) }, out int slot);
            WeaponSystem.Run(world, new[] { new SimInputFrame { EntityId = id } }, t);

            WeaponSystem.Run(world, new[]
            {
                new SimInputFrame
                {
                    EntityId = id, Buttons = SimInputFrame.ButtonSwitchWeapon,
                    SelectedWeaponSlot = 1, ActionSeq = 1u,
                },
            }, t);

            Assert.Equal(0, world.Entities[slot].SelectedWeapon);              // 拒绝面：选中槽不动
            Assert.Equal(WeaponSlotState.Ready, WeaponAt(world, slot, 0).State);
            Assert.Equal(WeaponSlotState.Unequipped, WeaponAt(world, slot, 1).State);
        }

        [Fact]
        public void 切枪后_半自动扳机待击_按住只发一发()
        {
            // 切枪置 armed=1（新武器扳机待击）——半自动边沿门与切枪的交叉面
            var (world, id, slot) = Spawn();
            Switch(world, id, to: 1);
            world.Frame = 40;
            WeaponSystem.Run(world, new[] { new SimInputFrame { EntityId = id } }, TwoSlotTable());

            int fires = 0;
            for (int f = 40; f < 100; f++) FireOnce(world, id, frame: f);       // 按住 60 帧
            fires = FireEvents(world);
            Assert.Equal(1, fires);                                            // 半自动：按住只发一发（霰弹 75rpm 48 帧节拍也在窗内只此一发）
            Assert.Equal(7, WeaponAt(world, slot, 1).MagAmmo);
        }
    }
}
