using LiteSim;
using LiteGame.View;
using LiteTesting;
using LiteTesting.Unity;
using NUnit.Framework;
using UnityEngine;
using LiteFramework;
using LiteGame;
using LiteGame.Modules.Input;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 瞄准激光驱动（<see cref="BattleLaserDriver"/>，修订口径：**激光器装在武器上**）的
    /// L2 EditMode 覆盖：束挂到武器挂载点（Weapon_Rifle/Muzzle）本地空间；**端点恒沿枪管前向**
    /// （瞄准/开火语境不收敛准心，不读 AimPoint）；截停走 <c>SimRaycast</c> 单源（实体圆柱三维 +
    /// 障碍折回同量纲 + 射程取最近）；可见性门（未对齐/死亡/被拦/未装备/不支持/**无挂载点**——
    /// 灰盒无武器即无激光，诚实退化）。
    ///
    /// **夹具坐标系（易踩）**：本地视图**自带朝向**（<c>SimView</c> 按模型前沿 +Z 施加
    /// <c>FacingRotation</c>，Yaw=0 时视图世界 yaw 已 90°），因此挂载点朝向一律用
    /// <see cref="BuildMount"/> 的**世界 yaw** 表述（默认 90° ⇒ 前向 +X 且本地 +Z = 枪管轴）；
    /// 用 <c>localRotation</c> 会两次相加得到世界 180°，射向与目标垂直、所有截停落空。
    /// 真 prefab/真枪口的手感对位归 PlayMode/手测。
    /// </summary>
    public sealed class BattleLaserEditModeTests : UnityTestBase
    {
        /// <summary>
        /// **静态态隔离（每例前置复位）**：<see cref="LaserSightPolicy.Supports"/> 在
        /// <c>TestModeRuntime.Current.Active</c> 置位时**绕过 def id 集合**直接返回快照开关——若前序用例
        /// （含同夹具的测试模式用例、或任何触发 <c>DevStartupConfig.ApplySnapshotOrDefaults</c> 的路径）
        /// 留下 Active=true，后续「不支持武器应隐藏」这类用例会拿到"支持"而被误判失败。
        /// 故每例开工前把该静态快照复位到**非测试模式默认口径**（用例内自行置位的仍可覆盖）。
        /// </summary>
        [SetUp]
        public void ResetTestModeSnapshot()
        {
            TestModeRuntime.Current.Active = false;
            TestModeRuntime.Current.LaserSight = true;
        }

        private static SimMapData NoObstacleMap() => new SimMapData();

        /// <summary>横墙（覆盖 z 向通道）：近面 = x − 0.5。</summary>
        private static SimMapData WallAt(float x, float height = 20f)
        {
            var m = new SimMapData();
            m.Obstacles[0] = new SimObstacle
            {
                Kind = SimObstacleKind.Box, Center = new SimVector3(x, 0f, 0f),
                HalfX = 0.5f, HalfZ = 20f, Height = height,
            };
            m.ObstacleCount = 1;
            return m;
        }

        /// <summary>本地实体 (0,0,0) + 逐参数追加目标；灰盒视图 + 本地对齐（同准心 EditMode 形态）。</summary>
        private (SimWorldState World, RollbackSim Sim, SimView View, GameObject LocalView) BuildBattle(
            SimMapData map, params Vector3[] others)
        {
            var world = new SimWorldState { RngState = 1UL };
            long self = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f) }, out _);
            foreach (Vector3 o in others)
                world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(o.x, o.y, o.z) }, out _);
            var sim = new RollbackSim(world, map, new[] { new SimInputFrame { EntityId = self } }, CombatValues.Default, WeaponTable.Default);
            SimView view = new SimView(world, null,
                factory: (loc, parent) => Scope.CreateGameObject(loc),
                recycler: null);
            view.AlignLocal(self);
            view.Tick(1f / 60f);                        // 首帧落位：HasLocalDisplay 置真 + 视图建立
            view.TryGetView(0, out var localView);
            return (world, sim, view, localView);
        }

        /// <summary>
        /// 逐分量断言（含容差）。**不用 <c>Assert.AreEqual(Vector3, Vector3)</c>**——那是精确相等，
        /// 三维求交链（归一化、InverseTransformPoint）必然产生浮点末位差异，会把语义正确的端点判成失败
        /// （典型表现：期望与实得打印完全一样却仍红）。
        /// </summary>
        private static void AssertLocal(Vector3 actual, Vector3 expected, float tol, string msg = null)
        {
            Assert.AreEqual(expected.x, actual.x, tol, msg + "（本地 x）");
            Assert.AreEqual(expected.y, actual.y, tol, msg + "（本地 y）");
            Assert.AreEqual(expected.z, actual.z, tol, msg + "（本地 z）");
        }

        /// <summary>
        /// 武器激光挂载点（本地视图下的 "Muzzle" 锚点），朝向按**世界 yaw** 给定（默认 90° = 前向 +X）。
        ///
        /// **为什么用 <c>transform.rotation</c> 而不是 <c>localRotation</c>**（坐标系陷阱）：
        /// 本地视图**自带朝向**——<c>SimView</c> 按模型视觉前沿约定 +Z 施加 <c>FacingRotation</c>
        /// （Sim Yaw=0 时视图世界 yaw 已是 90°）。若再叠一个 <c>localRotation</c>，两次旋转相加，
        /// 传 90° 会得到世界 yaw 180°（前向 −Z）⇒ 射向与目标垂直、所有截停落空跑满射程。
        /// 挂载点朝向必须以**世界**为准表述。
        ///
        /// **默认 90° 的意义**：使**本地 +Z = 枪管轴**（prefab <c>Weapon_Rifle/Muzzle</c> 的约定，
        /// 枪管模式按"本地 +Z × 距离"写端点）。收敛/回退模式用 <c>InverseTransformPoint</c>，
        /// 与挂载点朝向无关。
        /// </summary>
        private Transform BuildMount(GameObject localView, Vector3 localPos, float worldYawDeg = 90f)
        {
            var mountGo = Scope.CreateGameObject("Muzzle");
            mountGo.transform.SetParent(localView.transform, false);
            mountGo.transform.localPosition = localPos;
            mountGo.transform.rotation = Quaternion.Euler(0f, worldYawDeg, 0f);
            return mountGo.transform;
        }

        private (InputService Input, BattleLaserDriver Driver, LineRenderer Beam) BuildDriver(
            SimView view, RollbackSim sim, SimMapData map, MatchTestRules rules = null,
            LiteGame.IDevSession dev = null)
        {
            var input = new InputService();
            input.SetSource(new FixedAimSource());           // 只用被拦门；激光不读瞄准输入
            var beamGo = Scope.CreateGameObject("Beam", typeof(LineRenderer));
            LineRenderer beam = beamGo.GetComponent<LineRenderer>();
            var driver = new BattleLaserDriver(view, input, sim, map, beam, rules, dev);
            return (input, driver, beam);
        }
        /// <summary>生产序：渲染帧采样（门在此裁决）→ 上行取值（帧边界清）→ 供 Tick 消费。</summary>
        private static void SampleInput(InputService input)
        {
            input.SampleOnRenderFrame(default);
            input.TryTakeForSend(out _);
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 激光_束挂武器挂载点_空旷端点为射程()
        {
            var map = NoObstacleMap();
            var (world, sim, view, localView) = BuildBattle(map);
            Transform mount = BuildMount(localView, new Vector3(0f, 1f, 0f));   // 前向 = +X
            var (input, driver, beam) = BuildDriver(view, sim, map);
            _ = world;

            SampleInput(input);
            driver.Tick();

            Assert.IsTrue(beam.gameObject.activeSelf, "支持武器 + 有挂载点 → 显示");
            Assert.AreSame(mount, beam.transform.parent, "束实例挂在武器挂载点下（做在武器上）");
            Assert.AreEqual(Vector3.zero, beam.GetPosition(0));                 // 起点 = 挂载点本位
            AssertLocal(beam.GetPosition(1), new Vector3(0f, 0f, CombatConfig.HitscanRange), 1e-3f, "本地 +Z × 射程");
            driver.Dispose();
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 激光_玩家挡路_端点为圆柱近弧()
        {
            var map = NoObstacleMap();
            var (world, sim, view, localView) = BuildBattle(map, new Vector3(10f, 0f, 0f));
            BuildMount(localView, new Vector3(0f, 1f, 0f));                     // 原点 (0,1,0)、前向 +X
            var (input, driver, beam) = BuildDriver(view, sim, map);
            _ = world;

            SampleInput(input);
            driver.Tick();

            // 遇玩家截停：近弧 = 圆心距 − 命中半径 = 10 − 0.5
            AssertLocal(beam.GetPosition(1), new Vector3(0f, 0f, 10f - CombatConfig.HitscanRadius), 1e-3f);
            driver.Dispose();
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 激光_障碍更近_截停在障碍面()
        {
            var map = WallAt(5f);
            var (world, sim, view, localView) = BuildBattle(map, new Vector3(10f, 0f, 0f));
            BuildMount(localView, new Vector3(0f, 1f, 0f));
            var (input, driver, beam) = BuildDriver(view, sim, map);
            _ = world;

            SampleInput(input);
            driver.Tick();

            // 障碍（近面 4.5）比玩家（9.5）近 → 激光停墙——与权威子弹同一判定
            AssertLocal(beam.GetPosition(1), new Vector3(0f, 0f, 4.5f), 1e-3f);
            driver.Dispose();
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 激光_障碍挡在身后_不挡弹_玩家正常截停()
        {
            var map = WallAt(12f);                       // 墙近面 11.5 在玩家命中弧（9.5）之后
            var (world, sim, view, localView) = BuildBattle(map, new Vector3(10f, 0f, 0f));
            BuildMount(localView, new Vector3(0f, 1f, 0f));
            var (input, driver, beam) = BuildDriver(view, sim, map);
            _ = world;

            SampleInput(input);
            driver.Tick();
            AssertLocal(beam.GetPosition(1), new Vector3(0f, 0f, 10f - CombatConfig.HitscanRadius), 1e-3f);
            driver.Dispose();
        }
        [Test]
        [Category(TestCategory.Contract)]
        public void 激光_瞄准与开火语境_端点仍沿枪管不收敛准心()
        {
            var map = NoObstacleMap();
            var (world, sim, view, localView) = BuildBattle(map, new Vector3(10f, 0f, 0f));
            BuildMount(localView, new Vector3(0f, 1f, 0f));        // 枪口世界前向 +X，目标在 +X
            var (input, driver, beam) = BuildDriver(view, sim, map);

            SampleInput(input);
            driver.Tick();
            Vector3 barrel = beam.GetPosition(1);
            AssertLocal(barrel, new Vector3(0f, 0f, 10f - CombatConfig.HitscanRadius), 1e-3f, "待机：枪管截停目标");

            world.Entities[0].Flags |= EntityFlags.Aiming;
            SampleInput(input);
            driver.Tick();
            AssertLocal(beam.GetPosition(1), barrel, 1e-3f, "瞄准位不改变端点（不收敛准心）");

            world.Entities[0].Flags &= ~EntityFlags.Aiming;
            world.Entities[0].FireStanceFrames = (byte)CombatConfig.FireStanceFrames;
            SampleInput(input);
            driver.Tick();
            AssertLocal(beam.GetPosition(1), barrel, 1e-3f, "开火窗不改变端点（不收敛准心）");
            driver.Dispose();
        }


        [Test]
        [Category(TestCategory.Unit)]
        public void 激光_方向随枪不追鼠标_无输入也出束()
        {
            // 两侧各一个目标；枪口（挂载点）转向谁就打谁——端点与瞄准输入无关。
            // **两个子场景各自独立构造挂载点**（而非构造后改 rotation）：EditMode 下运行时改
            // Transform 朝向不会即时反映到同帧的 forward 读取，"转向"必须在 Tick 前就位——
            // 这与"美术转锚点即转向"一致（转的是 prefab 上的挂载点，不是运行期临时摆动）。
            var map = NoObstacleMap();
            var (world, sim, view, localView) = BuildBattle(map,
                new Vector3(10f, 0f, 0f), new Vector3(-10f, 0f, 0f));
            _ = world;

            BuildMount(localView, new Vector3(0f, 1f, 0f), worldYawDeg: 90f);      // 枪口朝 +X
            var beamGo = Scope.CreateGameObject("Beam", typeof(LineRenderer));
            var driver = new BattleLaserDriver(view, null, sim, map, beamGo.GetComponent<LineRenderer>());

            driver.Tick();                                // 无输入服务：不拦、不读瞄准——照样出束
            Assert.IsTrue(beamGo.activeSelf, "无输入形态也显示（方向来自枪，不来自输入/鼠标）");
            AssertLocal(beamGo.GetComponent<LineRenderer>().GetPosition(1),
                new Vector3(0f, 0f, 10f - CombatConfig.HitscanRadius), 1e-3f, "枪口朝 +X → 截停 +X 侧目标");
            driver.Dispose();

            BuildMount(localView, new Vector3(0f, 1f, 0f), worldYawDeg: -90f);     // 转锚点即转向 −X
            var beamGo2 = Scope.CreateGameObject("Beam", typeof(LineRenderer));
            var driver2 = new BattleLaserDriver(view, null, sim, map, beamGo2.GetComponent<LineRenderer>());

            driver2.Tick();
            AssertLocal(beamGo2.GetComponent<LineRenderer>().GetPosition(1),
                new Vector3(0f, 0f, 10f - CombatConfig.HitscanRadius), 1e-3f, "枪口朝 −X → 截停 −X 侧目标（端点随挂载点朝向，与鼠标无关）");
            driver2.Dispose();
        }
        [Test]
        [Category(TestCategory.Unit)]
        public void 激光_无挂载点_隐藏_灰盒诚实退化()
        {
            var map = NoObstacleMap();
            var (world, sim, view, _) = BuildBattle(map);             // 无 Muzzle 锚点（灰盒形态）
            var (input, driver, beam) = BuildDriver(view, sim, map);
            _ = world;

            SampleInput(input);
            driver.Tick();
            Assert.IsFalse(beam.gameObject.activeSelf, "武器上没有激光挂载点 → 无激光（不造第二视觉源）");
            driver.Dispose();
        }

        [Test]
        [Category(TestCategory.Unit)]
        public void 激光_不支持武器_隐藏()
        {
            var map = NoObstacleMap();
            var (world, sim, view, localView) = BuildBattle(map);
            BuildMount(localView, new Vector3(0f, 1f, 0f));
            world.Weapons[0].WeaponDefId = 999;           // 不在支持集合
            var (input, driver, beam) = BuildDriver(view, sim, map);

            SampleInput(input);
            driver.Tick();
            Assert.IsFalse(beam.gameObject.activeSelf, "def id 999 不支持 → 隐藏");
            driver.Dispose();
        }

        [Test]
        [Category(TestCategory.Unit)]
        public void 激光_测试模式_任何武器都支持_退出回策略()
        {
            var map = NoObstacleMap();
            var (world, sim, view, localView) = BuildBattle(map);
            BuildMount(localView, new Vector3(0f, 1f, 0f));
            world.Weapons[0].WeaponDefId = 999;
            var (input, driver, beam) = BuildDriver(view, sim, map, dev: new DevSession());   // 开发端口读 TestModeRuntime 快照

            try
            {
                TestModeRuntime.Current.Active = true;           // 测试模式直通——任何武器都支持
                SampleInput(input);
                driver.Tick();
                Assert.IsTrue(beam.gameObject.activeSelf, "测试模式 → 999 也支持");

                TestModeRuntime.Current.Active = false;          // 退出回策略口径
                SampleInput(input);
                driver.Tick();
                Assert.IsFalse(beam.gameObject.activeSelf, "退出测试模式 → 策略集合重新生效");
            }
            finally
            {
                TestModeRuntime.Current.Active = false;           // 静态态复位（勿泄漏进其他用例）
            }
            driver.Dispose();
        }

        [Test]
        [Category(TestCategory.Unit)]
        public void 激光_未装备武器_隐藏()
        {
            var map = NoObstacleMap();
            var (world, sim, view, localView) = BuildBattle(map);
            BuildMount(localView, new Vector3(0f, 1f, 0f));
            world.Entities[0].SelectedWeapon = -1;
            var (input, driver, beam) = BuildDriver(view, sim, map);

            SampleInput(input);
            driver.Tick();
            Assert.IsFalse(beam.gameObject.activeSelf, "SelectedWeapon=-1 → 隐藏");
            driver.Dispose();
        }

        [Test]
        [Category(TestCategory.Unit)]
        public void 激光_死亡_隐藏()
        {
            var map = NoObstacleMap();
            var (world, sim, view, localView) = BuildBattle(map);
            BuildMount(localView, new Vector3(0f, 1f, 0f));
            world.Entities[0].Hp = 0;
            var (input, driver, beam) = BuildDriver(view, sim, map);

            SampleInput(input);
            driver.Tick();
            Assert.IsFalse(beam.gameObject.activeSelf, "尸体不出激光");
            driver.Dispose();
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 激光_输入被拦_藏_解拦恢复()
        {
            var map = NoObstacleMap();
            var (world, sim, view, localView) = BuildBattle(map);
            BuildMount(localView, new Vector3(0f, 1f, 0f));
            var (input, driver, beam) = BuildDriver(view, sim, map);
            _ = world;

            bool modalOpen = false;
            input.RegisterBlocker(new IntentGate.BlockerKey("ui.modal", "模态打开"), () => modalOpen);

            modalOpen = true;
            SampleInput(input);
            driver.Tick();
            Assert.IsFalse(beam.gameObject.activeSelf, "被拦（模态 UI）→ 藏");

            modalOpen = false;
            SampleInput(input);
            driver.Tick();
            Assert.IsTrue(beam.gameObject.activeSelf, "解拦 → 恢复");
            driver.Dispose();
        }

        [Test]
        [Category(TestCategory.Unit)]
        public void 激光_本地表现未建_隐藏()
        {
            var map = NoObstacleMap();
            var world = new SimWorldState { RngState = 1UL };
            long self = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f) }, out _);
            var sim = new RollbackSim(world, map, new[] { new SimInputFrame { EntityId = self } }, CombatValues.Default, WeaponTable.Default);
            SimView view = new SimView(world, null,
                factory: (loc, parent) => Scope.CreateGameObject(loc),
                recycler: null);                           // 未 AlignLocal：LocalEntityId=0、HasLocalDisplay=false
            var (input, driver, beam) = BuildDriver(view, sim, map);

            SampleInput(input);
            driver.Tick();
            Assert.IsFalse(beam.gameObject.activeSelf, "本地表现未建 → 隐藏");
            driver.Dispose();
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 激光_规则走注入_不读活动局静态读口()
        {
            // 侧向 1.2m 的目标：默认双柱（身体 0.45 / 爆头 0.32、爆头线下沿 ~1.4）打不中；
            // 注入"爆头带下移到 0 + 爆头柱 1.5"才在 9.1m 处命中。两个方向各验一次：
            // ① 注入宽规则 → 命中（读注入）；② 不注入 → 默认几何（无全局"当前局"可借）。
            var map = NoObstacleMap();
            var (world, sim, view, localView) = BuildBattle(map, new Vector3(10f, 0f, 1.2f));
            BuildMount(localView, new Vector3(0f, 1f, 0f));                    // 枪口 (0,1,0) 前向 +X
            _ = world;

            var wideHead = new MatchTestRules { HeadHitLineOverride = 0f, HeadshotRadiusOverride = 1.5f };

            var (input1, driver1, beam1) = BuildDriver(view, sim, map, wideHead);
            SampleInput(input1);
            driver1.Tick();
            AssertLocal(beam1.GetPosition(1), new Vector3(0f, 0f, 10f - 0.9f), 1e-2f,
                "注入的宽爆头柱生效——端点不是射程上限");
            driver1.Dispose();

            var (input2, driver2, beam2) = BuildDriver(view, sim, map, rules: null);
            SampleInput(input2);
            driver2.Tick();
            AssertLocal(beam2.GetPosition(1), new Vector3(0f, 0f, CombatConfig.HitscanRange), 1e-2f,
                "未注入规则 → 默认几何（不存在可借用的全局当前局）");
            driver2.Dispose();
        }

        /// <summary>空载荷设备源替身（装配兼容保留——激光驱动不读瞄准输入，只读被拦门，此源不承载数据）。</summary>
        private sealed class FixedAimSource : IIntentSource
        {
            public string Name => "fixed-aim";
            public IntentSample Sample(in SimVector3 localPos) => new IntentSample(new SimInputFrame());
        }
    }
}
