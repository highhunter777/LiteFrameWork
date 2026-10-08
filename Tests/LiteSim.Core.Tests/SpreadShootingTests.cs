using LiteSim;
using Xunit;

namespace LiteSim.Tests
{
    /// <summary>
    /// 散布与多弹丸的**射击系统集成**用例（《游戏业务系统总设计》§5 散布/弹丸）：
    /// 经 <see cref="SimStep"/> 全链（InputSystem 置瞄准态 → 武器装备 → 弹丸环）验证——
    /// 随机消费口径（每弹丸 2 笔 + 每命中 ±1 一笔）、锥角上界的命中界、腰射放大、
    /// 霰弹多弹丸的伤害合计与逐弹丸障碍截停、无效开火零消耗。
    /// 纯几何件见 <see cref="SimSpreadTests"/>；半自动边沿门见 <see cref="SemiAutoTests"/>。
    /// </summary>
    public sealed class SpreadShootingTests
    {
        /// <summary>空障碍大图（50m 半宽——MovementSystem 会把实体钳进地图界，小图会把目标钳到原点）。</summary>
        private static readonly SimMapData NoObstacles = new SimMapData { GroundY = 0f, HalfWidth = 50f, HalfDepth = 50f };

        /// <summary>霰弹档（与 tb_weapon id=1 同形：8 弹丸/半自动/散布 3°——装载面钉子）。
        /// 槽 0 注册（懒装备直达）——切枪语义由 <see cref="WeaponSwitchTests"/> 钉，本组只验弹道。</summary>
        private static WeaponTable ShotgunSpread(float spread = 3f, bool automatic = false)
            => TestWeapons.WithRow(1, damage: 8, rpm: 75, magazineSize: 8, reserveAmmo: 24,
                reloadFrames: 168, range: 30f, spread: spread, pellets: 8, switchFrames: 40,
                automatic: automatic, slot: 0);

        private static (SimWorldState world, long shooter, long target, int targetSlot)
            SpawnPair(float distance, ulong seed = 99UL, int targetHp = 1000)
        {
            var world = new SimWorldState { RngState = seed };
            long shooter = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f) }, out _);
            long target = world.Spawn(new EntitySlot { Hp = targetHp, Pos = new SimVector3(distance, 0f, 0f) }, out int ts);
            return (world, shooter, target, ts);
        }

        private static SimInputFrame[] FireAt(long id, float aimX, bool aiming)
            => new[] { new SimInputFrame
            {
                EntityId = id, AimPointX = aimX, AimPointY = 1f, AimPointZ = 0f,
                Buttons = SimInputFrame.ButtonFire | (aiming ? SimInputFrame.ButtonAim : 0u),
            } };

        private static int CountKind(SimWorldState s, FrameEventKind k0, FrameEventKind k1)
        {
            int n = 0;
            for (int i = 0; i < s.Events.Count; i++)
            {
                FrameEventKind k = s.Events.Items[i].Kind;
                if (k == k0 || k == k1) n++;
            }
            return n;
        }

        private static int HitValueSum(SimWorldState s)
        {
            int sum = 0;
            for (int i = 0; i < s.Events.Count; i++)
            {
                FrameEventKind k = s.Events.Items[i].Kind;
                if (k == FrameEventKind.Hit || k == FrameEventKind.Crit) sum += s.Events.Items[i].Value;
            }
            return sum;
        }

        /// <summary>推进本地 RngState 期望值（同式复刻射击系统消费序：散布 n×2 笔 + 命中每笔 ±1）。</summary>
        private static ulong Advance(ulong state, int pelletRolls, int damageRolls)
        {
            var rng = new SimRng(state);
            for (int i = 0; i < pelletRolls; i++) { rng.NextFloat01(); rng.NextFloat01(); }
            for (int i = 0; i < damageRolls; i++) rng.NextRange(-1, 2);
            return rng.State;
        }

        [Fact]
        public void 散布0_直射命中_随机仅伤害浮动一笔()
        {
            var (world, shooter, _, _) = SpawnPair(20f);
            ulong rng0 = world.RngState;
            WeaponTable table = TestWeapons.NoSpread();

            SimStep.Step(world, NoObstacles, FireAt(shooter, 20f, aiming: false), CombatValues.Default, table);

            Assert.Equal(1, CountKind(world, FrameEventKind.Fire, FrameEventKind.Fire));
            Assert.Equal(1, CountKind(world, FrameEventKind.Hit, FrameEventKind.Crit));
            Assert.Equal(Advance(rng0, 0, 1), world.RngState);       // spread=0 ⇒ 无散布笔，仅命中 ±1 一笔
        }

        [Fact]
        public void 散布0_脱靶_随机零消耗()
        {
            var (world, shooter, target, targetSlot) = SpawnPair(20f);
            ulong rng0 = world.RngState;
            var fire = new[] { new SimInputFrame
            {
                EntityId = shooter, AimPointX = 0f, AimPointY = 1f, AimPointZ = 50f,   // 90° 侧向——必脱靶
                Buttons = SimInputFrame.ButtonFire,
            } };

            SimStep.Step(world, NoObstacles, fire, CombatValues.Default, TestWeapons.NoSpread());

            Assert.Equal(1, CountKind(world, FrameEventKind.Fire, FrameEventKind.Fire));
            Assert.Equal(0, CountKind(world, FrameEventKind.Hit, FrameEventKind.Crit));
            Assert.Equal(rng0, world.RngState);                      // 脱靶零消耗（散布 0 + 无命中）
            Assert.Equal(1000, world.Entities[targetSlot].Hp);
            _ = target;
        }

        [Fact]
        public void 瞄准散布锥内上界_20m必命中_随机每弹丸两笔加命中一笔()
        {
            // 锥角上界语义的集成面：1.2° 最大偏转 @20m ≤ 0.42m < 命中柱 0.45 ⇒ ADS 必命中（30 种子）
            for (int seed = 1; seed <= 30; seed++)
            {
                var (world, shooter, _, _) = SpawnPair(20f, (ulong)(seed * 7919));
                ulong rng0 = world.RngState;

                SimStep.Step(world, NoObstacles, FireAt(shooter, 20f, aiming: true),
                    CombatValues.Default, WeaponTable.Default);

                Assert.True(CountKind(world, FrameEventKind.Hit, FrameEventKind.Crit) == 1,
                    $"seed={seed}：瞄准 1.2° @20m 应必命中（锥内上界）");
                Assert.Equal(Advance(rng0, 1, 1), world.RngState);   // 每弹丸 2 笔（偏转角+方位角）+ 命中 1 笔
            }
        }

        [Fact]
        public void 腰射散布4倍_20m远必有命中与脱靶分岔()
        {
            // 腰射 = 表值 × HipSpreadFactor(4) = 4.8° @20m 最大 1.68m：锥角覆盖命中柱内外 ⇒ 分岔面
            int hits = 0, misses = 0;
            for (int seed = 1; seed <= 60; seed++)
            {
                var (world, shooter, _, _) = SpawnPair(20f, (ulong)(seed * 104729));
                SimStep.Step(world, NoObstacles, FireAt(shooter, 20f, aiming: false),
                    CombatValues.Default, WeaponTable.Default);
                if (CountKind(world, FrameEventKind.Hit, FrameEventKind.Crit) > 0) hits++;
                else misses++;
            }
            Assert.True(hits > 0, "腰射 60 种子内应有命中（4.8° 锥内仍有近轴概率）");
            Assert.True(misses > 0, "腰射 60 种子内应有脱靶（4.8° 锥覆盖柱外——放大语义");
        }

        [Fact]
        public void 霰弹8弹丸_近距全命中_一次Fire八Hit_伤害合计等于HP扣减()
        {
            var (world, shooter, target, targetSlot) = SpawnPair(5f);
            ulong rng0 = world.RngState;
            WeaponTable sg = ShotgunSpread();

            // 半自动：按住（连续位）本帧只此一发——击发沿语义见 SemiAutoTests
            SimStep.Step(world, NoObstacles, FireAt(shooter, 5f, aiming: true), CombatValues.Default, sg);

            Assert.Equal(1, CountKind(world, FrameEventKind.Fire, FrameEventKind.Fire));       // 扣一次扳机 = 一个 Fire
            Assert.Equal(8, CountKind(world, FrameEventKind.Hit, FrameEventKind.Crit));        // 8 弹丸各自命中

            int sum = HitValueSum(world);
            Assert.InRange(sum, 8 * (8 - 1), 8 * (8 + 1));                                      // 每弹丸 8±1
            Assert.Equal(1000 - sum, world.Entities[targetSlot].Hp);                            // 伤害合计 = HP 扣减

            // 弹药/序号：一次扣扳机只消费一发（8 弹丸共享该发）
            ref WeaponRuntime w = ref world.Weapons[0 * SimConfig.WeaponSlotsPerEntity + world.Entities[0].SelectedWeapon];
            Assert.Equal(7, w.MagAmmo);
            Assert.Equal(1, w.ShotSeq);

            // 随机消费：8 弹丸 × 2 笔 + 8 命中 × 1 笔
            Assert.Equal(Advance(rng0, 8, 8), world.RngState);
            _ = target;
        }

        [Fact]
        public void 霰弹8弹丸_障碍更近逐弹丸截停_全零穿透()
        {
            var (world, shooter, target, targetSlot) = SpawnPair(5f);
            var map = new SimMapData { GroundY = 0f, HalfWidth = 50f, HalfDepth = 50f };
            map.Obstacles[0] = new SimObstacle
            {
                Kind = SimObstacleKind.Circle,
                Center = new SimVector3(3f, 0f, 0f),
                Radius = 1.5f,
                Height = 5f,
            };
            map.ObstacleCount = 1;
            ulong rng0 = world.RngState;

            SimStep.Step(world, map, FireAt(shooter, 5f, aiming: true), CombatValues.Default, ShotgunSpread());

            Assert.Equal(1, CountKind(world, FrameEventKind.Fire, FrameEventKind.Fire));
            Assert.Equal(0, CountKind(world, FrameEventKind.Hit, FrameEventKind.Crit));        // 全部被墙截停
            Assert.Equal(1000, world.Entities[targetSlot].Hp);
            Assert.Equal(Advance(rng0, 8, 0), world.RngState);                                 // 散布笔照消费、无命中笔
            _ = target;
        }

        [Fact]
        public void 无效开火_无点帧_零随机消耗_仅Fire事件()
        {
            var (world, shooter, _, _) = SpawnPair(20f);
            ulong rng0 = world.RngState;
            var fire = new[] { new SimInputFrame { EntityId = shooter, Buttons = SimInputFrame.ButtonFire } };

            SimStep.Step(world, NoObstacles, fire, CombatValues.Default, WeaponTable.Default);

            Assert.Equal(1, CountKind(world, FrameEventKind.Fire, FrameEventKind.Fire));
            Assert.Equal(0, CountKind(world, FrameEventKind.Hit, FrameEventKind.Crit));
            Assert.Equal(rng0, world.RngState);                     // 无点 = 无效开火：零随机消费
        }
    }
}
