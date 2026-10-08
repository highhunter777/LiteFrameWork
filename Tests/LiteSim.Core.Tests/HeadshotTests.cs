using LiteSim;
using Xunit;

namespace LiteSim.Tests
{
    /// <summary>
    /// 爆头判定与伤害倍率：命中高度带（弹道 = 逻辑枪口 → AimPoint；爆头判据 = 判定高度落
    /// 头部带 [<see cref="CombatConfig.HeadHitLine"/>, <see cref="CombatConfig.HitscanHeight"/>]）× `2^HeadshotDamageShift` 位级倍率——
    /// 倍率在命中判定处应用，Damage 命令/Hit 事件携带即最终值，结算侧零改动。
    /// 死亡守卫半边（同批）：死亡射手不开火 / 死亡目标不可命中 / 死亡实体输入作废（尸体不受操控）。
    /// </summary>
    public sealed class HeadshotTests
    {
        private static (SimWorldState world, long shooter, long target, int shooterSlot, int targetSlot)
            SpawnPair(float shooterY, float targetY, int targetHp = 100)
        {
            var world = new SimWorldState { RngState = 1UL };
            long shooter = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, shooterY, 0f) }, out int ss);
            long target = world.Spawn(new EntitySlot { Hp = targetHp, Pos = new SimVector3(10f, targetY, 0f) }, out int ts);
            return (world, shooter, target, ss, ts);
        }

        private static SimInputFrame[] FireAt(long id, float px = 10f, float py = 1f, float pz = 0f)
            => new[] { new SimInputFrame { EntityId = id, AimPointX = px, AimPointY = py, AimPointZ = pz, Buttons = SimInputFrame.ButtonFire } };

        /// <summary>空障碍图（本组只验命中高度/倍率/死亡守卫——不参与障碍遮挡判定）。</summary>
        private static readonly SimMapData NoObstacles = new SimMapData();

        /// 命中事件的伤害值（取第一个指定 Kind 的事件；未找到 = -1）。
        /// <paramref name="kind"/> 默认取<see cref="FrameEventKind.Hit"/>——**爆头事件是
        /// <see cref="FrameEventKind.Crit"/>**（三维化裁决，《固定斜视角射击方案专项设计》§5），
        /// 故爆头用例须显式传 Crit 取值，否则读不到（-1）。
        private static int EventValue(SimWorldState world, FrameEventKind kind = FrameEventKind.Hit)
        {
            for (int i = 0; i < world.Events.Count; i++)
                if (world.Events.Items[i].Kind == kind)
                    return world.Events.Items[i].Value;
            return -1;
        }

        /// <summary>命中事件的伤害值（默认口径：任一命中类事件——Hit 或 Crit，兼容旧断言）。</summary>
        private static int HitValue(SimWorldState world)
        {
            for (int i = 0; i < world.Events.Count; i++)
            {
                FrameEventKind k = world.Events.Items[i].Kind;
                if (k == FrameEventKind.Hit || k == FrameEventKind.Crit)
                    return world.Events.Items[i].Value;
            }
            return -1;
        }

        private static int DamageAmount(SimWorldState world)
        {
            for (int i = 0; i < world.Cmds.Count; i++)
                if (world.Cmds.Items[i].Kind == SimCommandKind.Damage)
                    return world.Cmds.Items[i].Amount;
            return -1;
        }

        [Fact]
        public void 同地平面对枪_命中高度不达爆头带_基础伤害()
        {
            var (world, shooter, target, _, _) = SpawnPair(shooterY: 0f, targetY: 0f);

            ShootingSystem.Run(world, NoObstacles, FireAt(shooter), CombatValues.Default, WeaponTable.Default);

            int hit = HitValue(world);
            Assert.InRange(hit, CombatConfig.BaseDamage - CombatConfig.DamageSpread,
                CombatConfig.BaseDamage + CombatConfig.DamageSpread);
            Assert.Equal(hit, DamageAmount(world));
            Assert.Equal(2, world.Events.Count);           // Fire + Hit（无 Death——目标未死）
        }

        [Fact]
        public void 高差位命中头部带_倍率移位生效_伤害为偶数区间()
        {
            // 射手高台（Y=1）→ 枪口高 ≈2.29；瞄准点取 (10,1.7)（烘焙身高 1.8 的新头部带 [1.395,1.8] 内）
            // ⇒ 弹道自枪口俯射、命中且判定高度 = AimPoint.Y = 1.7 ≥ HeadHitLine → 爆头。
            var (world, shooter, target, _, _) = SpawnPair(shooterY: 1f, targetY: 0f);

            ShootingSystem.Run(world, NoObstacles, FireAt(shooter, py: 1.7f), CombatValues.Default, WeaponTable.Default);

            int hit = HitValue(world);
            int lower = (CombatConfig.BaseDamage - CombatConfig.DamageSpread) << CombatConfig.HeadshotDamageShift;
            int upper = (CombatConfig.BaseDamage + CombatConfig.DamageSpread) << CombatConfig.HeadshotDamageShift;
            Assert.InRange(hit, lower, upper);
            Assert.Equal(0, hit % (1 << CombatConfig.HeadshotDamageShift));   // 移位后的位级痕迹
            Assert.Equal(hit, DamageAmount(world));
            // **爆头事件必须是 Crit**（三维化裁决：爆头发 Crit、普通发 Hit——表现层靠它分档飘红字）
            Assert.True(HasEvent(world, FrameEventKind.Crit), "爆头应产出 Crit 事件（普通命中才是 Hit）");
            Assert.False(HasEvent(world, FrameEventKind.Hit), "爆头不应同时产出普通 Hit 事件");
        }

        /// <summary>是否产出指定 Kind 的事件。</summary>
        private static bool HasEvent(SimWorldState world, FrameEventKind kind)
        {
            for (int i = 0; i < world.Events.Count; i++)
                if (world.Events.Items[i].Kind == kind) return true;
            return false;
        }

        /// <summary>
        /// **平地爆头**（本批核心目标，《固定斜视角射击方案专项设计》§5）：
        /// 瞄准点口径：准心压在目标头部 ⇒ 判定点（AimPoint）落进头部带 ⇒ 爆头
        /// （点口径前方向-近弧口径在头部下沿会差出带外）。
        /// </summary>
        [Fact]
        public void 平地远距_准心压在头部_命中头部带爆头()
        {
            // 射手平地（Y=0，枪口高 ≈1.29）；目标在 X=10。准心（AimPoint）压在目标头部带内 ≈1.8m
            // ⇒ 弹道自枪口抬向该点 ⇒ 命中点落进头部带（[1.55, 2.0]）。
            var world = new SimWorldState { RngState = 1UL };
            long shooter = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f) }, out _);
            long target = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(10f, 0f, 0f) }, out _);

            ShootingSystem.Run(world, NoObstacles, FireAt(shooter, px: 10f, py: 1.8f, pz: 0f), CombatValues.Default, WeaponTable.Default);

            Assert.True(HasEvent(world, FrameEventKind.Crit),
                $"平地远距 + 仰角应爆头（事件={DescribeKinds(world)}）");
            int crit = EventValue(world, FrameEventKind.Crit);
            int lower = (CombatConfig.BaseDamage - CombatConfig.DamageSpread) << CombatConfig.HeadshotDamageShift;
            int upper = (CombatConfig.BaseDamage + CombatConfig.DamageSpread) << CombatConfig.HeadshotDamageShift;
            Assert.InRange(crit, lower, upper);
        }

        /// <summary>
        /// 平地**准心压在身位中部**（AimPoint.y=1.0）⇒ 判定高度 1.0 &lt; 1.55，不进头部带 ⇒ **不爆头**
        /// （对照组：证明爆头来自仰角而非"三维化让所有命中都变爆头"）。
        /// </summary>
        [Fact]
        public void 平地近距_无仰角_不进头部带_普通命中()
        {
            var (world, shooter, target, _, _) = SpawnPair(shooterY: 0f, targetY: 0f);

            ShootingSystem.Run(world, NoObstacles, FireAt(shooter), CombatValues.Default, WeaponTable.Default);   // 点取身位中部（py=1）⇒ 判定高度 1.0

            Assert.False(HasEvent(world, FrameEventKind.Crit),
                $"平地水平弹道不应爆头（事件={DescribeKinds(world)}）");
            int hit = EventValue(world, FrameEventKind.Hit);
            Assert.InRange(hit, CombatConfig.BaseDamage - CombatConfig.DamageSpread,
                CombatConfig.BaseDamage + CombatConfig.DamageSpread);
        }

        /// <summary>
        /// **陡俯角近距离必须能命中**（区间闸回归，《固定斜视角射击方案专项设计》§5）：
        /// 俯视相机射线自上方斜下，射入圆柱的"近弧"高度可能已高于柱顶，但射线继续下降确实穿过柱体。
        /// 旧单点 Y 闸只看近弧高度 ⇒ 判"掠过头顶"而**完全打不中**（实测俯角 50°/相机 10m/目标 8m：
        /// 近弧高 2.003 &gt; 柱顶 2.0 被拒）。修复后按区间交集判定 ⇒ 命中且落进头部带。
        ///
        /// 反例锁定：把单点闸改回 ⇒ 射手在 (−6.43, 7.66)、目标在 8m、射线向下命中头部带，
        /// 本用例与下一条（大俯角普通命中）同时转红。
        /// </summary>
        [Fact]
        public void 大俯角近距离_射线自上方斜下_仍命中头部带()
        {
            var world = new SimWorldState { RngState = 1UL };
            // 射手高踞 (−10, 6)：枪口（本体+枪口高度 1.0）落在 (−9.65, 7.0)，俯视角相机射线
            // 自上方斜下的等价几何。目标在原点 ⇒ 水平距离 ≈9.65m、落差 ≈5.2m（陡俯角）。
            long shooter = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(-10f, 6f, 0f) }, out _);
            long target = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f) }, out _);

            // 瞄准点 = 目标头部带中心 (0, 1.85, 0)——自枪口斜下的陡俯角弹道（与相机解算的几何同构）
            ShootingSystem.Run(world, NoObstacles, FireAt(shooter, px: 0f, py: 1.85f, pz: 0f), CombatValues.Default, WeaponTable.Default);

            Assert.True(HasEvent(world, FrameEventKind.Crit),
                $"大俯角近距离应命中头部带爆头（事件={DescribeKinds(world)}）");
            int crit = EventValue(world, FrameEventKind.Crit);
            int lower = (CombatConfig.BaseDamage - CombatConfig.DamageSpread) << CombatConfig.HeadshotDamageShift;
            int upper = (CombatConfig.BaseDamage + CombatConfig.DamageSpread) << CombatConfig.HeadshotDamageShift;
            Assert.InRange(crit, lower, upper);
            _ = target;
        }

        /// <summary>
        /// **AimPoint 口径的核心契约**：准心射线命中头部下沿 ⇒ <b>必爆头</b>（所见即所判）。
        ///
        /// 这正是方向口径做不到的：方向口径下服务器从枪口<b>沿方向</b>求交，命中点是圆柱<b>近弧</b>，
        /// 高度比准心命中点略低——俯角 30°/目标 20m/准心恰在下沿 1.70m 时命中 ≈1.67m ⇒ 跌出爆头带。
        /// AimPoint 口径下服务器「从枪口<b>指向 P</b>」求交，命中点**就是 P**。
        ///
        /// 反例锁定：把 ShootingSystem 改回"沿方向求交" ⇒ 本用例转红（命中点落到带外）。
        /// </summary>
        [Fact]
        public void AimPoint口径_准心命中头部下沿_必爆头()
        {
            var world = new SimWorldState { RngState = 1UL };
            long shooter = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f) }, out _);
            world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(20f, 0f, 0f) }, out _);

            // 准心射线命中点P：目标正前方、**恰好在头部带下沿**（含端点，闭区间）
            float pY = CombatConfig.HeadHitLine;
            ShootingSystem.Run(world, NoObstacles, new[] { new SimInputFrame
            {
                EntityId = shooter,
                AimPointX = 20f, AimPointY = pY, AimPointZ = 0f,
                Buttons = SimInputFrame.ButtonFire,
            } }, CombatValues.Default, WeaponTable.Default);

            Assert.True(HasEvent(world, FrameEventKind.Crit),
                $"AimPoint 恰在下沿应爆头（事件={DescribeKinds(world)}）");
            int crit = EventValue(world, FrameEventKind.Crit);
            int lower = (CombatConfig.BaseDamage - CombatConfig.DamageSpread) << CombatConfig.HeadshotDamageShift;
            int upper = (CombatConfig.BaseDamage + CombatConfig.DamageSpread) << CombatConfig.HeadshotDamageShift;
            Assert.InRange(crit, lower, upper);
        }

        /// <summary>
        /// AimPoint 口径下**掩体遮挡**：目标在点之后，但中间有墙⇒ 打墙（不穿墙、不爆头）。
        /// 遮挡由「从枪口指向 P」的射线上障碍更近承担（与激光同源同向）。
        /// </summary>
        [Fact]
        public void AimPoint口径_中间有墙_打墙不爆头()
        {
            var map = new SimMapData();
            map.Obstacles[0] = new SimObstacle
            {
                Kind = SimObstacleKind.Box, Center = new SimVector3(10f, 0f, 0f),
                HalfX = 0.5f, HalfZ = 20f, Height = 20f,
            };
            map.ObstacleCount = 1;

            var world = new SimWorldState { RngState = 1UL };
            long shooter = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f) }, out _);
            long target = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(20f, 0f, 0f) }, out _);

            ShootingSystem.Run(world, map, new[] { new SimInputFrame
            {
                EntityId = shooter,
                AimPointX = 20f, AimPointY = CombatConfig.HeadHitLine, AimPointZ = 0f,
                Buttons = SimInputFrame.ButtonFire,
            } }, CombatValues.Default, WeaponTable.Default);

            Assert.False(HasEvent(world, FrameEventKind.Crit), "墙在前⇒ 不应爆头（子弹不穿墙）");
            Assert.False(HasEvent(world, FrameEventKind.Hit), "墙在前⇒ 不应命中墙后目标");
            Assert.Equal(-1, DamageAmount(world));
            _ = target;
        }

        /// <summary>
        /// **自动验证件所用的那条契约**：AimPoint 压在头部带<b>中心</b> ⇒ 必爆头（最大容错）。
        /// 端到端链路上任何一环把 AimPoint 送歪（协议丢字段/闸门拒收/回溯没带），
        /// 都会在这里或真机对局里表现为"打不出爆头"。
        /// </summary>
        [Fact]
        public void AimPoint口径_压头部带中心_必爆头()
        {
            var world = new SimWorldState { RngState = 1UL };
            long shooter = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f) }, out _);
            world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(15f, 0f, 0f) }, out _);

            float headCenter = CombatConfig.HeadHitLine
                + (CombatConfig.HitscanHeight - CombatConfig.HeadHitLine) * 0.5f;

            ShootingSystem.Run(world, NoObstacles, new[] { new SimInputFrame
            {
                EntityId = shooter,
                AimPointX = 15f, AimPointY = headCenter, AimPointZ = 0f,
                Buttons = SimInputFrame.ButtonFire,
            } }, CombatValues.Default, WeaponTable.Default);

            Assert.True(HasEvent(world, FrameEventKind.Crit),
                $"压头部带中心应爆头（事件={DescribeKinds(world)}，中心高 {headCenter}）");
            int crit = EventValue(world, FrameEventKind.Crit);
            int lower = (CombatConfig.BaseDamage - CombatConfig.DamageSpread) << CombatConfig.HeadshotDamageShift;
            int upper = (CombatConfig.BaseDamage + CombatConfig.DamageSpread) << CombatConfig.HeadshotDamageShift;
            Assert.InRange(crit, lower, upper);
        }

        /// <summary>事件 Kind 列表（失败消息辅助——让"为什么没爆头"一眼可读）。</summary>
        private static string DescribeKinds(SimWorldState world)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < world.Events.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(world.Events.Items[i].Kind);
            }
            return sb.ToString();
        }

        [Fact]
        public void 死亡射手不开火_零事件零命令()
        {
            var (world, shooter, target, shooterSlot, _) = SpawnPair(shooterY: 0f, targetY: 0f);
            world.Entities[shooterSlot].Hp = 0;            // 死亡事实（Hp≤0）

            ShootingSystem.Run(world, NoObstacles, FireAt(shooter), CombatValues.Default, WeaponTable.Default);

            Assert.Equal(0, world.Events.Count);
            Assert.Equal(0, world.Cmds.Count);
        }

        [Fact]
        public void 死亡目标不可命中_无命中无伤害()
        {
            var (world, shooter, target, _, _) = SpawnPair(shooterY: 0f, targetY: 0f, targetHp: 0);

            ShootingSystem.Run(world, NoObstacles, FireAt(shooter), CombatValues.Default, WeaponTable.Default);

            Assert.Equal(1, world.Events.Count);           // 只有 Fire（无 Hit）
            Assert.Equal(FrameEventKind.Fire, world.Events.Items[0].Kind);
            Assert.Equal(-1, DamageAmount(world));
        }

        [Fact]
        public void 死亡实体输入作废_移动朝向开火窗标志位全不写()
        {
            var world = new SimWorldState { RngState = 1UL };
            long id = world.Spawn(new EntitySlot { Hp = 0, Pos = new SimVector3(0f, 0f, 0f) }, out int slot);

            InputSystem.Run(world, new[] { new SimInputFrame
            {
                EntityId = id, MoveX = 1f, AimPointX = 1f, AimPointY = 1f, AimPointZ = 0f,
                Buttons = SimInputFrame.ButtonAim | SimInputFrame.ButtonFire,
            } }, CombatValues.Default);

            Assert.Equal(0f, world.Entities[slot].Vel.X, 4);
            Assert.Equal(0, world.Entities[slot].FireStanceFrames);
            Assert.Equal(0u, world.Entities[slot].Flags & EntityFlags.Aiming);
        }
    }
}
