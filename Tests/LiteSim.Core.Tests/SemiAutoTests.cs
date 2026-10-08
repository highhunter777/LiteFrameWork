using LiteSim;
using Xunit;

namespace LiteSim.Tests
{
    /// <summary>
    /// 半自动击发边沿门用例（《游戏业务系统总设计》§5 击发模式）：
    /// <c>fire_mode = semi</c>（<see cref="WeaponDef.Automatic"/> = false）——按住只发一发；
    /// 击发沿消费与冷却独立（冷却内按压同样消费沿、按住不放冷却到点不自动补发）；
    /// 松开帧重臂；自动武器整段旁路。armed 状态的 checksum 层级见 SimLayoutContractTests。
    /// </summary>
    public sealed class SemiAutoTests
    {
        private static readonly SimMapData NoObstacles = new SimMapData();

        /// <summary>半自动步枪档（快节拍 6 帧便于冷却窗用例；零散布）。</summary>
        private static WeaponTable SemiTable(int intervalFrames = 6)
        {
            int rpm = SimConfig.TickRate * 60 / intervalFrames;     // 与 FireIntervalFrames 反推一致（600rpm @6 帧）
            return TestWeapons.WithRow(0, damage: 16, rpm: rpm, magazineSize: 30, reserveAmmo: 90,
                reloadFrames: 132, range: 100f, spread: 0f, pellets: 1, switchFrames: 30,
                automatic: false, slot: 0);
        }

        private static (SimWorldState world, long id, int slot) Spawn(WeaponTable table)
        {
            var world = new SimWorldState { RngState = 1UL };
            long id = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f) }, out int slot);
            WeaponSystem.Run(world, new[] { new SimInputFrame { EntityId = id } }, table);   // 懒装备 + armed=1
            return (world, id, slot);
        }

        /// <summary>跑一帧（按住/松开开火由 held 决定）——WeaponSystem 重臂 + ShootingSystem 消费。</summary>
        private static void RunFrame(SimWorldState world, long id, int frame, bool held, WeaponTable table)
        {
            world.Frame = frame;
            SimInputFrame input = held
                ? new SimInputFrame { EntityId = id, AimPointX = 50f, AimPointY = 1f, AimPointZ = 0f, Buttons = SimInputFrame.ButtonFire }
                : new SimInputFrame { EntityId = id };
            var inputs = new[] { input };
            WeaponSystem.Run(world, inputs, table);
            ShootingSystem.Run(world, NoObstacles, inputs, CombatValues.Default, table);
        }

        private static int FireEvents(SimWorldState s)
        {
            int n = 0;
            for (int i = 0; i < s.Events.Count; i++)
                if (s.Events.Items[i].Kind == FrameEventKind.Fire) n++;
            return n;
        }

        [Fact]
        public void 按住只发一发_松开帧重臂_再按发第二发()
        {
            WeaponTable table = SemiTable();
            var (world, id, slot) = Spawn(table);

            for (int f = 0; f < 12; f++) RunFrame(world, id, f, held: true, table);
            Assert.Equal(1, FireEvents(world));                       // 按住 12 帧（>节拍 6）只此一发
            Assert.Equal(29, world.Weapons[slot * SimConfig.WeaponSlotsPerEntity].MagAmmo);
            Assert.Equal(0, world.Entities[slot].SemiFireArmed);      // 击发沿已消费

            RunFrame(world, id, 12, held: false, table);               // 松开帧：重臂
            Assert.Equal(1, world.Entities[slot].SemiFireArmed);

            RunFrame(world, id, 13, held: true, table);                // 再按（节拍 6 已过）→ 第二发
            Assert.Equal(2, FireEvents(world));
            Assert.Equal(28, world.Weapons[slot * SimConfig.WeaponSlotsPerEntity].MagAmmo);
        }

        [Fact]
        public void 冷却内按压消费击发沿_按住不放冷却到点不自动补发()
        {
            WeaponTable table = SemiTable();
            var (world, id, slot) = Spawn(table);

            RunFrame(world, id, 0, held: true, table);                 // 首发沿：击发（armed 1→0）
            Assert.Equal(1, FireEvents(world));

            // 期间松开-再按（节拍 4 帧处、未到 6）：沿被消费但节拍拦截——不击发
            RunFrame(world, id, 1, held: false, table);
            RunFrame(world, id, 4, held: true, table);
            Assert.Equal(1, FireEvents(world));                        // 节拍未到——不击发（沿已消费）
            Assert.Equal(0, world.Entities[slot].SemiFireArmed);

            // 按住不放跨过节拍点（6 帧）：半自动不补发（要求逐次按压）
            for (int f = 5; f < 10; f++) RunFrame(world, id, f, held: true, table);
            Assert.Equal(1, FireEvents(world));

            // 松开-再按：节拍早过 → 击发第二发
            RunFrame(world, id, 10, held: false, table);
            RunFrame(world, id, 11, held: true, table);
            Assert.Equal(2, FireEvents(world));
        }

        [Fact]
        public void 换弹中按压消费沿_换弹完成需松开再按()
        {
            WeaponTable table = SemiTable();
            var (world, id, slot) = Spawn(table);
            ref WeaponRuntime w = ref world.Weapons[slot * SimConfig.WeaponSlotsPerEntity];
            w.MagAmmo = 0;
            w.ReserveAmmo = 90;
            world.Frame = 0;
            WeaponSystem.Run(world, new[]
            {
                new SimInputFrame { EntityId = id, Buttons = SimInputFrame.ButtonReload, ActionSeq = 1u },
            }, table);
            Assert.Equal(WeaponSlotState.Reloading, w.State);

            // 换弹中按压：沿被消费（换弹完成按住不会立即打空第一发）
            RunFrame(world, id, 1, held: true, table);
            Assert.Equal(0, world.Entities[slot].SemiFireArmed);
            Assert.Equal(0, FireEvents(world));

            // 按住到换弹完成帧（132）：完成同帧**不击发**（沿已消费）——与自动武器"完成同帧续射"分野
            for (int f = 2; f <= 132; f++) RunFrame(world, id, f, held: true, table);
            Assert.Equal(0, FireEvents(world));
            Assert.Equal(WeaponSlotState.Ready, w.State);               // 换弹完成（132 帧完成）
            Assert.Equal(30, w.MagAmmo);

            // 松开重臂后再按 → 击发
            RunFrame(world, id, 140, held: false, table);
            RunFrame(world, id, 141, held: true, table);
            Assert.Equal(1, FireEvents(world));
        }

        [Fact]
        public void 自动武器不受边沿门影响_按住连发()
        {
            var world = new SimWorldState { RngState = 1UL };
            long id = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f) }, out int slot);
            WeaponTable table = TestWeapons.NoSpread();                 // 步枪 auto（600rpm 6 帧）
            WeaponSystem.Run(world, new[] { new SimInputFrame { EntityId = id } }, table);

            for (int f = 0; f < 30; f++) RunFrame(world, id, f, held: true, table);
            Assert.Equal(5, FireEvents(world));                        // 0/6/12/18/24 帧各一发——连发不受 armed 门
            _ = slot;
        }
    }
}
