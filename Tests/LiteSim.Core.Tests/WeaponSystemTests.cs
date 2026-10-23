using LiteSim;
using Xunit;

namespace LiteSim.Tests
{
    /// <summary>
    /// 武器系统（《游戏业务系统总设计》§5 武器系统方案；tb_weapon 表驱）：
    /// 懒装备（首生/复活自动装备默认步枪满弹）、射速节拍（<c>ceil(TickRate×60/rpm)</c> 整数帧）、
    /// 弹匣扣减与打空、换弹（时长/弹药转移/换弹中不可开火/半满规避）、**打空自动换弹**（末发同帧进入）、
    /// 武器门在一切副作用之前。切枪见 <see cref="WeaponSwitchTests"/>、半自动见 <see cref="SemiAutoTests"/>、
    /// 散布/多弹丸见 <see cref="SpreadShootingTests"/>。
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
        public void 武器试验覆写_解析口无覆写回表值_覆写生效()
        {
            Assert.Equal(16, MatchTestRules.ResolveWeaponDamage(null, 16));
            Assert.Equal(16, MatchTestRules.ResolveWeaponDamage(new MatchTestRules(), 16));
            Assert.Equal(6, MatchTestRules.ResolveFireIntervalFrames(null, 600, 6));
            Assert.Equal(6, MatchTestRules.ResolveFireIntervalFrames(new MatchTestRules(), 600, 6));
            Assert.Equal(6, MatchTestRules.ResolveFireIntervalFrames(
                new MatchTestRules { RpmOverride = 0 }, 600, 6));              // ≤ 0 = 未覆写（防除零回表值）

            var rules = new MatchTestRules { DamageOverride = 50, RpmOverride = 1200 };
            Assert.Equal(50, MatchTestRules.ResolveWeaponDamage(rules, 16));
            Assert.Equal(3, MatchTestRules.ResolveFireIntervalFrames(rules, 600, 6));   // 1200rpm @60Hz ⇒ 3 帧
        }

        [Fact]
        public void 武器试验覆写_开火链消费覆写节拍()
        {
            var (world, id, slot) = Spawn();
            var rules = new MatchTestRules { RpmOverride = 1200 };
            WeaponSystem.Run(world, Idle(id), WeaponTable.Default, rules);
            ref WeaponRuntime w = ref WeaponOf(world, slot);

            world.Frame = 0;
            ShootingSystem.Run(world, NoObstacles, Fire(id), CombatValues.Default, WeaponTable.Default, rules);
            Assert.Equal(1, FireEvents(world));
            Assert.Equal(3, w.NextFireFrame);                      // 覆写节拍 3 帧（表值 6）

            world.Frame = 3;                                       // 覆写节拍到帧 → 第二发（表值要等第 6 帧）
            ShootingSystem.Run(world, NoObstacles, Fire(id), CombatValues.Default, WeaponTable.Default, rules);
            Assert.Equal(2, FireEvents(world));
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
        public void 长按射击_节拍稳定_打空自动换弹_到帧完成按住自动续射()
        {
            var (world, id, slot) = Spawn();
            ref WeaponRuntime w = ref WeaponOf(world, slot);
            WeaponDef def = WeaponConfig.Default;
            var fire = Fire(id);

            // ① 按住 10s（600 帧）：600rpm ⇒ 每 6 帧一发；30 发打空（174 帧）→ **自动换弹**（打空即补），
            //    306 帧完成同帧续射第二梭（306..480）→ 第 60 发打空再入自动换弹。
            //    帧内两梭正好跨自动换弹窗：600 帧末仍处第二次换弹（ReloadEndFrame=480+132=612）。
            for (int f = 0; f < 600; f++)
            {
                world.Frame = f;
                WeaponSystem.Run(world, fire, WeaponTable.Default);
                ShootingSystem.Run(world, NoObstacles, fire, CombatValues.Default, WeaponTable.Default);
            }
            Assert.Equal(60, FireEvents(world));                  // 两梭整发（0..174 + 306..480）
            Assert.Equal(0, w.MagAmmo);                            // 第 60 发打空
            Assert.Equal(60, w.ReserveAmmo);                       // 转移尚未发生（完成帧才转移：90−30）
            Assert.Equal(WeaponSlotState.Reloading, w.State);      // 打空自动换弹进行中
            Assert.Equal(480 + def.ReloadFrames, w.ReloadEndFrame);

            // ② 按住过完换弹窗：静默至完成帧；到帧完成同帧自动续射（换弹先回满——WeaponSystem 先跑）
            int before = FireEvents(world);
            for (int f = 600; f < 612; f++)
            {
                world.Frame = f;
                WeaponSystem.Run(world, fire, WeaponTable.Default);
                ShootingSystem.Run(world, NoObstacles, fire, CombatValues.Default, WeaponTable.Default);
            }
            Assert.Equal(before, FireEvents(world));              // 换弹窗内静默

            world.Frame = 612;
            WeaponSystem.Run(world, fire, WeaponTable.Default);
            ShootingSystem.Run(world, NoObstacles, fire, CombatValues.Default, WeaponTable.Default);
            Assert.Equal(before + 1, FireEvents(world));           // 完成同帧续射首发
            Assert.Equal(def.MagazineSize - 1, w.MagAmmo);
            Assert.Equal(30, w.ReserveAmmo);                       // 第二次转移：60−30
            Assert.Equal(WeaponSlotState.Ready, w.State);

            // ③ 续射节拍照旧：+6 帧内不发、到点即发（600rpm 不被换弹打乱）
            for (int f = 613; f < 618; f++)
            {
                world.Frame = f;
                WeaponSystem.Run(world, fire, WeaponTable.Default);
                ShootingSystem.Run(world, NoObstacles, fire, CombatValues.Default, WeaponTable.Default);
            }
            Assert.Equal(before + 1, FireEvents(world));           // 节拍未到——不发
            world.Frame = 618;
            WeaponSystem.Run(world, fire, WeaponTable.Default);
            ShootingSystem.Run(world, NoObstacles, fire, CombatValues.Default, WeaponTable.Default);
            Assert.Equal(before + 2, FireEvents(world));           // 节拍到点——续射第二发
        }

        [Fact]
        public void 自动换弹_末发同帧进入Reloading_完成帧转移备弹()
        {
            var (world, id, slot) = Spawn();
            WeaponSystem.Run(world, Idle(id), WeaponTable.Default);
            ref WeaponRuntime w = ref WeaponOf(world, slot);
            WeaponDef def = WeaponConfig.Default;
            w.MagAmmo = 1;

            world.Frame = 10;
            WeaponSystem.Run(world, Fire(id), WeaponTable.Default);
            ShootingSystem.Run(world, NoObstacles, Fire(id), CombatValues.Default, WeaponTable.Default);

            Assert.Equal(1, FireEvents(world));                    // 末发照常击发
            Assert.Equal(0, w.MagAmmo);
            Assert.Equal(WeaponSlotState.Reloading, w.State);      // 打空自动换弹：同帧进入
            Assert.Equal(10 + def.ReloadFrames, w.ReloadEndFrame);
            Assert.Equal(def.ReserveAmmo, w.ReserveAmmo);          // 完成帧才转移——此刻备弹未动

            world.Frame = 10 + def.ReloadFrames;
            WeaponSystem.Run(world, Idle(id), WeaponTable.Default);
            Assert.Equal(WeaponSlotState.Ready, w.State);
            Assert.Equal(def.MagazineSize, w.MagAmmo);
            Assert.Equal(def.ReserveAmmo - def.MagazineSize, w.ReserveAmmo);
        }

        [Fact]
        public void 自动换弹_零备弹不触发_空仓静默()
        {
            var (world, id, slot) = Spawn();
            WeaponSystem.Run(world, Idle(id), WeaponTable.Default);
            ref WeaponRuntime w = ref WeaponOf(world, slot);
            w.MagAmmo = 1;
            w.ReserveAmmo = 0;

            world.Frame = 10;
            WeaponSystem.Run(world, Fire(id), WeaponTable.Default);
            ShootingSystem.Run(world, NoObstacles, Fire(id), CombatValues.Default, WeaponTable.Default);
            Assert.Equal(1, FireEvents(world));
            Assert.Equal(0, w.MagAmmo);
            Assert.Equal(WeaponSlotState.Ready, w.State);           // 备弹零：不自动换弹（空仓静默）

            world.Frame = 20;
            WeaponSystem.Run(world, Fire(id), WeaponTable.Default);
            ShootingSystem.Run(world, NoObstacles, Fire(id), CombatValues.Default, WeaponTable.Default);
            Assert.Equal(1, FireEvents(world));                     // 空仓：不开火（无 Fire 事件）
        }

        [Fact]
        public void 空仓扣扳机_判据统一置窗_朝准星当帧限速_静默无事件()
        {
            var (world, id, slot) = Spawn();
            WeaponSystem.Run(world, Idle(id), WeaponTable.Default);
            ref WeaponRuntime w = ref WeaponOf(world, slot);
            w.MagAmmo = 0;
            w.ReserveAmmo = 0;                                   // 彻底干涸：不自动换弹、无 Fire 可产

            // 扣扳机帧（准星 -X、移动 +X——反向场景）：三系统按序（输入 → 武器 → 射击）
            var fire = new[] { new SimInputFrame { EntityId = id, MoveX = 1f, AimPointX = -10f, AimPointY = 1f, Buttons = SimInputFrame.ButtonFire } };
            world.Frame = 10;
            InputSystem.Run(world, fire, CombatValues.Default);
            WeaponSystem.Run(world, fire, WeaponTable.Default);
            ShootingSystem.Run(world, NoObstacles, fire, CombatValues.Default, WeaponTable.Default);

            Assert.Equal(0, FireEvents(world));                  // 空仓静默：武器门拦在一切副作用之前
            Assert.True(world.Entities[slot].FireStanceFrames == (byte)CombatConfig.FireStanceFrames,
                "扣扳机即置窗（窗谓词与朝向派生同一条——不问武器门结果）");
            Assert.Equal(SimTrig.Atan2(0f, -1f), world.Entities[slot].Yaw, 5);   // 朝准星
            Assert.Equal(CombatConfig.AimMoveSpeed, world.Entities[slot].Vel.X, 4);   // 当帧限速（置窗先于限速行）

            // 间隙帧（松开）：窗自然衰减、窗内不回移动向——视图 AimWalk 四向稳定的前提
            var gap = new[] { new SimInputFrame { EntityId = id, MoveX = 1f, AimPointX = -10f, AimPointY = 1f } };
            world.Frame = 11;
            InputSystem.Run(world, gap, CombatValues.Default);
            Assert.True(world.Entities[slot].FireStanceFrames == (byte)(CombatConfig.FireStanceFrames - 1),
                "松开帧起窗逐帧自然衰减");
            Assert.Equal(SimTrig.Atan2(0f, -1f), world.Entities[slot].Yaw, 5);   // 窗内保持准星向

            // 换弹中扣扳机：同样进射击语境、同样静默（状态门拦——无事件）
            w.MagAmmo = 0;
            w.ReserveAmmo = 30;
            w.State = WeaponSlotState.Reloading;
            w.ReloadEndFrame = 10 + WeaponConfig.Default.ReloadFrames;
            world.Frame = 12;
            InputSystem.Run(world, fire, CombatValues.Default);
            WeaponSystem.Run(world, fire, WeaponTable.Default);
            ShootingSystem.Run(world, NoObstacles, fire, CombatValues.Default, WeaponTable.Default);
            Assert.Equal(0, FireEvents(world));
            Assert.True(world.Entities[slot].FireStanceFrames == (byte)CombatConfig.FireStanceFrames,
                "换弹中扣扳机同进射击语境（窗在场）");
        }

        [Fact]
        public void 无限子弹_连射不扣弹_不触发末发自动换弹_换弹请求被拒()
        {
            var (world, id, slot) = Spawn();
            WeaponSystem.Run(world, Idle(id), WeaponTable.Default);
            ref WeaponRuntime w = ref WeaponOf(world, slot);
            WeaponDef def = WeaponConfig.Default;

            // 测试规则经注入（每局持有件——无静态污染面；ShootingSystem 沿传 WeaponSystem）
            var rules = new MatchTestRules { InfiniteAmmo = true };
            for (int i = 0; i < def.MagazineSize * 2; i++)      // 连射两倍弹匣量
            {
                world.Frame = i * def.FireIntervalFrames;
                ShootingSystem.Run(world, NoObstacles, Fire(id), CombatValues.Default, WeaponTable.Default, rules);
            }
            Assert.Equal(def.MagazineSize, w.MagAmmo);          // 弹匣不降
            Assert.Equal(WeaponSlotState.Ready, w.State);       // 也不进入末发自动换弹
            Assert.Equal(def.ReserveAmmo, w.ReserveAmmo);       // 备弹未动
            Assert.Equal(def.MagazineSize * 2, FireEvents(world));   // 每发都真打出

            world.Frame += 1;
            WeaponSystem.Run(world, Reload(id), WeaponTable.Default, rules);
            Assert.Equal(WeaponSlotState.Ready, w.State);       // 换弹请求：满弹被拒（不进入 Reloading）
            Assert.Equal(def.MagazineSize, w.MagAmmo);
        }

        [Fact]
        public void 无限子弹_空仓放行_干涸弹匣照常开火不扣弹_零备弹换弹仍拒()
        {
            var (world, id, slot) = Spawn();
            WeaponSystem.Run(world, Idle(id), WeaponTable.Default);
            ref WeaponRuntime w = ref WeaponOf(world, slot);
            WeaponDef def = WeaponConfig.Default;

            // 彻底干涸：弹匣 0 + 备弹 0（打空自动换弹也救不回的形态）——GM 现场开无限子弹的场景
            w.MagAmmo = 0;
            w.ReserveAmmo = 0;

            var rules = new MatchTestRules { InfiniteAmmo = true };
            for (int i = 0; i < 3; i++)
            {
                world.Frame = i * def.FireIntervalFrames;
                ShootingSystem.Run(world, NoObstacles, Fire(id), CombatValues.Default, WeaponTable.Default, rules);
            }
            Assert.Equal(3, FireEvents(world));                   // 空仓门对无限子弹放行——干涸枪当场复活
            Assert.Equal(0, w.MagAmmo);                           // 不扣弹（无弹可扣、也不凭空造弹）
            Assert.Equal(0, w.ReserveAmmo);
            Assert.Equal(WeaponSlotState.Ready, w.State);         // 不进末发自动换弹（没消费过末发）

            world.Frame += 1;
            WeaponSystem.Run(world, Reload(id), WeaponTable.Default, rules);
            Assert.Equal(WeaponSlotState.Ready, w.State);         // 换弹门不受无限子弹影响：零备弹仍拒
        }

        [Fact]
        public void 换弹公共摘要_开始写入Reload_完成清空()
        {
            var (world, id, slot) = Spawn();
            WeaponSystem.Run(world, Idle(id), WeaponTable.Default);
            ref WeaponRuntime w = ref WeaponOf(world, slot);
            ref ActionRuntime act = ref world.Actions[slot * SimConfig.ActionSlotsPerEntity];
            WeaponDef def = WeaponConfig.Default;

            Assert.Equal(ActionIds.None, act.ActionId);              // 初始：无动作

            w.MagAmmo = def.MagazineSize - 5;
            world.Frame = 100;
            WeaponSystem.Run(world, Reload(id), WeaponTable.Default);
            Assert.Equal(ActionIds.Reload, act.ActionId);            // 换弹开始：主动作摘要写入（远端换弹表现载体）
            Assert.Equal(100, act.StartFrame);
            Assert.Equal(ActionPhase.Active, act.Phase);

            world.Frame = 100 + def.ReloadFrames;
            WeaponSystem.Run(world, Idle(id), WeaponTable.Default);
            Assert.Equal(ActionIds.None, act.ActionId);              // 换弹完成：公共动作摘要清空
            Assert.Equal(ActionPhase.None, act.Phase);
        }

        /// <summary>双槽表（切枪打断换弹用例需要槽 1 有默认行——<see cref="WeaponTable.Default"/> 只注册槽 0）。</summary>
        private static WeaponTable TwoSlotTable()
        {
            var t = new WeaponTable();
            WeaponDef rif = WeaponConfig.DefaultRifle;
            t.SetRow(rif.Id, rif.Damage, rif.Rpm, rif.MagazineSize, rif.ReserveAmmo, rif.MaxReserveAmmo,
                rif.ReloadFrames, rif.Range, 0f, rif.Pellets, rif.SwitchFrames, rif.Automatic);
            t.SetRow(1, 8, 75, 8, 24, 24, 168, 30f, 0f, 8, 40, false);
            t.SetSlotDefault(0, rif.Id);
            t.SetSlotDefault(1, 1);
            return t;
        }

        [Fact]
        public void 换弹公共摘要_切枪打断即清空_不回落永久换弹()
        {
            var (world, id, slot) = Spawn();
            WeaponTable table = TwoSlotTable();
            WeaponSystem.Run(world, Idle(id), table);
            ref WeaponRuntime w = ref WeaponOf(world, slot);
            ref ActionRuntime act = ref world.Actions[slot * SimConfig.ActionSlotsPerEntity];
            WeaponDef def = WeaponConfig.Default;

            w.MagAmmo = def.MagazineSize - 5;
            world.Frame = 50;
            WeaponSystem.Run(world, Reload(id), table);
            Assert.Equal(ActionIds.Reload, act.ActionId);

            // 切枪打断换弹（SwitchWeapon 意图，目标槽 1）——摘要随之清空（远端不得停在上一次换弹）
            var sw = new[] { new SimInputFrame { EntityId = id, Buttons = SimInputFrame.ButtonSwitchWeapon, SelectedWeaponSlot = 1, ActionSeq = 1u } };
            WeaponSystem.Run(world, sw, table);
            Assert.Equal(ActionIds.None, act.ActionId);
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
