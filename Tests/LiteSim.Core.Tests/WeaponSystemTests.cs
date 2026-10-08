using LiteSim;
using Xunit;

namespace LiteSim.Tests
{
    /// <summary>
    /// 武器系统（《游戏业务系统总设计》§5 武器系统方案；tb_weapon 表驱）：
    /// 懒装备（首生/复活自动装备默认步枪满弹）、射速节拍（<c>ceil(TickRate×60/rpm)</c> 整数帧）、
    /// 弹匣扣减与打空、换弹（时长/弹药转移/换弹中不可开火/半满规避）、武器门在一切副作用之前。
    /// **未装备实体**（直调 ShootingSystem 的旧形态）不经武器门——由既有用例回归覆盖。
    /// </summary>
    public sealed class WeaponSystemTests
    {
        private static readonly SimMapData NoObstacles = new SimMapData();

        private static SimInputFrame[] Fire(long id)
            => new[] { new SimInputFrame { EntityId = id, AimPointX = 100f, AimPointY = 1f, AimPointZ = 0f, Buttons = SimInputFrame.ButtonFire } };

        private static SimInputFrame[] Reload(long id)
            => new[] { new SimInputFrame { EntityId = id, Buttons = SimInputFrame.ButtonReload, ActionSeq = 1u } };

        private static SimInputFrame[] Idle(long id)
            => new[] { new SimInputFrame { EntityId = id } };

        private static int FireEvents(SimWorldState s)
        {
            int n = 0;
            for (int i = 0; i < s.Events.Count; i++)
                if (s.Events.Items[i].Kind == FrameEventKind.Fire) n++;
            return n;
        }

        private static (SimWorldState world, long id, int slot) Spawn()
        {
            var world = new SimWorldState { RngState = 1UL };
            long id = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f) }, out int slot);
            return (world, id, slot);
        }

        private static ref WeaponRuntime WeaponOf(SimWorldState s, int slot)
            => ref s.Weapons[slot * SimConfig.WeaponSlotsPerEntity + s.Entities[slot].SelectedWeapon];

        [Fact]
        public void 懒装备_首帧装备默认步枪_满弹就绪()
        {
            var (world, id, slot) = Spawn();
            WeaponSystem.Run(world, Idle(id), WeaponTable.Default);

            ref WeaponRuntime w = ref WeaponOf(world, slot);
            WeaponDef def = WeaponConfig.Default;
            Assert.Equal(WeaponSlotState.Ready, w.State);
            Assert.Equal(def.Id, w.WeaponDefId);
            Assert.Equal(def.MagazineSize, w.MagAmmo);
            Assert.Equal(def.ReserveAmmo, w.ReserveAmmo);
        }

        [Fact]
        public void 节拍_600rpm为6帧_连帧开火只首帧打出()
        {
            var (world, id, slot) = Spawn();
            WeaponSystem.Run(world, Idle(id), WeaponTable.Default);
            ref WeaponRuntime w = ref WeaponOf(world, slot);
            Assert.Equal(6, WeaponConfig.Default.FireIntervalFrames);   // 600rpm @60Hz ⇒ 6 帧/发

            for (int f = 0; f < 5; f++)
            {
                world.Frame = f;
                ShootingSystem.Run(world, NoObstacles, Fire(id), CombatValues.Default, WeaponTable.Default);
            }
            Assert.Equal(1, FireEvents(world));                    // 5 帧窗口内仅首帧真开火
            Assert.Equal(WeaponConfig.Default.MagazineSize - 1, w.MagAmmo);

            world.Frame = 6;                                       // 节拍到帧 → 第二发
            ShootingSystem.Run(world, NoObstacles, Fire(id), CombatValues.Default, WeaponTable.Default);
            Assert.Equal(2, FireEvents(world));
            Assert.Equal(WeaponConfig.Default.MagazineSize - 2, w.MagAmmo);
        }

        [Fact]
        public void 弹匣_打空后不开火_无Fire事件()
        {
            var (world, id, slot) = Spawn();
            WeaponSystem.Run(world, Idle(id), WeaponTable.Default);
            ref WeaponRuntime w = ref WeaponOf(world, slot);
            w.MagAmmo = 1;
            w.NextFireFrame = 0;

            world.Frame = 10;
            ShootingSystem.Run(world, NoObstacles, Fire(id), CombatValues.Default, WeaponTable.Default);
            Assert.Equal(1, FireEvents(world));
            Assert.Equal(0, w.MagAmmo);

            world.Frame = 20;
            ShootingSystem.Run(world, NoObstacles, Fire(id), CombatValues.Default, WeaponTable.Default);
            Assert.Equal(1, FireEvents(world));                    // 打空：不开火（不写 Fire 事件）
        }

        [Fact]
        public void 换弹_时长到帧完成_弹匣回满备弹扣减_换弹中不可开火()
        {
            var (world, id, slot) = Spawn();
            WeaponSystem.Run(world, Idle(id), WeaponTable.Default);
            ref WeaponRuntime w = ref WeaponOf(world, slot);
            WeaponDef def = WeaponConfig.Default;

            int fired = 10;
            w.MagAmmo = def.MagazineSize - fired;

            world.Frame = 100;
            WeaponSystem.Run(world, Reload(id), WeaponTable.Default);
            Assert.Equal(WeaponSlotState.Reloading, w.State);
            Assert.Equal(100 + def.ReloadFrames, w.ReloadEndFrame);

            // 换弹中开火被拦（副作用之前——无 Fire 事件、不扣弹）
            ShootingSystem.Run(world, NoObstacles, Fire(id), CombatValues.Default, WeaponTable.Default);
            Assert.Equal(0, FireEvents(world));
            Assert.Equal(def.MagazineSize - fired, w.MagAmmo);

            // 到帧前 1 帧仍未完成；到帧完成转移弹药
            world.Frame = 100 + def.ReloadFrames - 1;
            WeaponSystem.Run(world, Idle(id), WeaponTable.Default);
            Assert.Equal(WeaponSlotState.Reloading, w.State);

            world.Frame = 100 + def.ReloadFrames;
            WeaponSystem.Run(world, Idle(id), WeaponTable.Default);
            Assert.Equal(WeaponSlotState.Ready, w.State);
            Assert.Equal(def.MagazineSize, w.MagAmmo);
            Assert.Equal(def.ReserveAmmo - fired, w.ReserveAmmo);
        }

        [Fact]
        public void 换弹_备弹不足只装余量()
        {
            var (world, id, slot) = Spawn();
            WeaponSystem.Run(world, Idle(id), WeaponTable.Default);
            ref WeaponRuntime w = ref WeaponOf(world, slot);
            WeaponDef def = WeaponConfig.Default;

            w.MagAmmo = 0;
            w.ReserveAmmo = 5;
            world.Frame = 0;
            WeaponSystem.Run(world, Reload(id), WeaponTable.Default);
            world.Frame = def.ReloadFrames;
            WeaponSystem.Run(world, Idle(id), WeaponTable.Default);

            Assert.Equal(5, w.MagAmmo);
            Assert.Equal(0, w.ReserveAmmo);
            Assert.Equal(WeaponSlotState.Ready, w.State);
        }

        [Fact]
        public void 换弹_满弹与零备弹不触发()
        {
            var (world, id, slot) = Spawn();
            WeaponSystem.Run(world, Idle(id), WeaponTable.Default);
            ref WeaponRuntime w = ref WeaponOf(world, slot);

            world.Frame = 0;
            WeaponSystem.Run(world, Reload(id), WeaponTable.Default);                   // 满弹 → 不换
            Assert.Equal(WeaponSlotState.Ready, w.State);

            w.MagAmmo = 0;
            w.ReserveAmmo = 0;
            WeaponSystem.Run(world, Reload(id), WeaponTable.Default);                   // 零备弹 → 不换
            Assert.Equal(WeaponSlotState.Ready, w.State);
        }

        [Fact]
        public void 死亡_不装备不换弹_复活重装备()
        {
            var (world, id, slot) = Spawn();
            world.Entities[slot].Hp = 0;
            WeaponSystem.Run(world, new[] { Idle(id)[0], Reload(id)[0] }, WeaponTable.Default);
            Assert.Equal(WeaponSlotState.Unequipped, WeaponOf(world, slot).State);

            // 复活（槽位换 Id 重生的等价形态）：Despawn 清武器行 → 再跑装备
            world.Despawn(id);
            long reborn = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(1f, 0f, 1f) }, out int slot2);
            WeaponSystem.Run(world, Idle(reborn), WeaponTable.Default);
            ref WeaponRuntime w = ref WeaponOf(world, slot2);
            Assert.Equal(WeaponSlotState.Ready, w.State);
            Assert.Equal(WeaponConfig.Default.MagazineSize, w.MagAmmo);
        }

        [Fact]
        public void 长按射击_节拍稳定_打空静默_换弹后按住自动续射()
        {
            var (world, id, slot) = Spawn();
            ref WeaponRuntime w = ref WeaponOf(world, slot);
            WeaponDef def = WeaponConfig.Default;
            var fire = Fire(id);

            // ① 按住 10s（600 帧）：600rpm ⇒ 每 6 帧一发；30 发打空后静默（无 Fire 事件、不留负弹）
            for (int f = 0; f < 600; f++)
            {
                world.Frame = f;
                WeaponSystem.Run(world, fire, WeaponTable.Default);
                ShootingSystem.Run(world, NoObstacles, fire, CombatValues.Default, WeaponTable.Default);
            }
            Assert.Equal(def.MagazineSize, FireEvents(world));      // 30 发封顶
            Assert.Equal(0, w.MagAmmo);
            Assert.Equal(WeaponSlotState.Ready, w.State);           // 打空不自动换弹（归输入）

            // ② 按住不放 + 按一次 R：换弹期零开火（武器门拦在一切副作用之前）
            int before = FireEvents(world);
            var reloadHeld = new[]
            {
                new SimInputFrame
                {
                    EntityId = id, AimPointX = 100f, AimPointY = 1f, AimPointZ = 0f,
                    Buttons = SimInputFrame.ButtonFire | SimInputFrame.ButtonReload, ActionSeq = 1u,
                },
            };
            world.Frame = 600;
            WeaponSystem.Run(world, reloadHeld, WeaponTable.Default);
            ShootingSystem.Run(world, NoObstacles, reloadHeld, CombatValues.Default, WeaponTable.Default);
            Assert.Equal(WeaponSlotState.Reloading, w.State);
            Assert.Equal(before, FireEvents(world));

            for (int f = 601; f <= 600 + def.ReloadFrames; f++)     // 含到帧完成帧（732）
            {
                world.Frame = f;
                WeaponSystem.Run(world, fire, WeaponTable.Default);
                ShootingSystem.Run(world, NoObstacles, fire, CombatValues.Default, WeaponTable.Default);
            }
            // 到帧完成同帧：换弹先回满（WeaponSystem 在 ShootingSystem 之前跑）→ 按住立即续射首发
            Assert.Equal(before + 1, FireEvents(world));
            Assert.Equal(def.MagazineSize - 1, w.MagAmmo);

            // ③ 续射节拍照旧：+6 帧内不发、到点即发（600rpm 不被换弹打乱）
            for (int f = 733; f < 738; f++)
            {
                world.Frame = f;
                WeaponSystem.Run(world, fire, WeaponTable.Default);
                ShootingSystem.Run(world, NoObstacles, fire, CombatValues.Default, WeaponTable.Default);
            }
            Assert.Equal(before + 1, FireEvents(world));            // 节拍未到——不发
            world.Frame = 738;
            WeaponSystem.Run(world, fire, WeaponTable.Default);
            ShootingSystem.Run(world, NoObstacles, fire, CombatValues.Default, WeaponTable.Default);
            Assert.Equal(before + 2, FireEvents(world));            // 节拍到点——续射第二发
        }

        [Fact]
        public void 未装备实体_不经武器门_维持旧路径()
        {
            // 直调 ShootingSystem（不跑 WeaponSystem）＝测试/沙盒形态：装备前逐帧可开火（旧行为）
            var (world, id, _) = Spawn();
            for (int f = 0; f < 5; f++)
            {
                world.Frame = f;
                ShootingSystem.Run(world, NoObstacles, Fire(id), CombatValues.Default, WeaponTable.Default);
            }
            Assert.Equal(5, FireEvents(world));
        }
    }
}
