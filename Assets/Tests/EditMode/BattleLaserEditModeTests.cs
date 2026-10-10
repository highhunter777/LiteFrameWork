using System;
using LiteSim;
using LiteView;
using LiteTesting;
using LiteTesting.Unity;
using NUnit.Framework;
using UnityEngine;
using LiteClient;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 瞄准激光驱动（<see cref="BattleLaserDriver"/>，修订口径：**激光器装在武器上**）的
    /// L2 EditMode 覆盖：束挂到武器挂载点（Weapon_Rifle/Muzzle）本地空间；**两种端点模式**
    /// （收敛=逻辑枪口→瞄准目标点的三维弹道终点／枪管=挂载点前向——AimPoint 单口径后方向回退已退役）；
    /// 截停走 <c>SimRaycast</c> 单源（实体圆柱三维 + 障碍折回同量纲 + 射程取最近）；
    /// 可见性门（未对齐/死亡/被拦/未装备/不支持/**无挂载点**——灰盒无武器即无激光，诚实退化）。
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
        /// <c>TestModeRuntime.Active</c> 置位时**绕过 def id 集合**直接返回快照开关——若前序用例
        /// （含同夹具的测试模式用例、或任何触发 <c>DebugTuner.ApplySnapshotOrDefaults</c> 的路径）
        /// 留下 Active=true，后续「不支持武器应隐藏」这类用例会拿到"支持"而被误判失败。
        /// 故每例开工前把该静态快照复位到**非测试模式默认口径**（用例内自行置位的仍可覆盖）。
        /// </summary>
        [SetUp]
        public void ResetTestModeSnapshot()
        {
            TestModeRuntime.Active = false;
            TestModeRuntime.LaserSight = true;
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
            SimView view, RollbackSim sim, SimMapData map, Func<Vector3?> aimPointOf = null)
        {
            var input = new InputService();
            input.SetSource(new FixedAimSource());           // 兼装配：单口径下端点只由瞄准点注入源决定
            var beamGo = Scope.CreateGameObject("Beam", typeof(LineRenderer));
            LineRenderer beam = beamGo.GetComponent<LineRenderer>();
            var driver = new BattleLaserDriver(view, input, sim, map, beam, aimPointOf);
            return (input, driver, beam);
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 激光_瞄准语境端点直落准心地面点()
        {
            // 收敛模式（瞄准语境 + 瞄准点注入可用）：端点 = 准心标记的地面点（同源注入）——点恰好压在准心上
            var map = NoObstacleMap();
            var (world, sim, view, localView) = BuildBattle(map);
            BuildMount(localView, new Vector3(1f, 1f, 0f));
            Vector3 aimPoint = new Vector3(10.35f, 0f, -0.2f);   // 准心射线在目标身上的点（带高度）
            var (input, driver, beam) = BuildDriver(view, sim, map, () => aimPoint);
            world.Entities[0].Flags |= EntityFlags.Aiming;

            SampleInput(input);
            driver.Tick();

            // 三维化：端点 = 弹道终点（逻辑枪口 → 瞄准目标点），无遮挡 ⇒ 端点恰为该点（世界 (10.35, 0, −0.2)）。
            // 挂载点世界位 = 视图 yaw90 作用于本地 (1,1,0) = (0,1,−1)；本地坐标经 InverseTransformPoint 表达。
            Vector3 p1 = beam.GetPosition(1);
            Assert.IsTrue(beam.gameObject.activeSelf, "支持武器 + 挂载点 + 瞄准点 → 显示");
            Assert.AreEqual(-0.8f, p1.x, 1e-3f, "端点=瞄准目标点（挂载点本地 x）");
            Assert.AreEqual(-1f, p1.y, 1e-3f, "束落到目标点高度 0（挂载点本地 y：0−1）");
            Assert.AreEqual(10.35f, p1.z, 1e-3f, "沿枪管轴的本地 z 分量（目标点距挂载点）");
            driver.Dispose();
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 激光_瞄准点被玩家挡路_端点截停在玩家近弧()
        {
            // 三维化：端点 = 弹道终点（逻辑枪口 → 瞄准点，沿途取最近交点）。
            // bot 在 (5.85, 0, −0.2)，枪口（烘焙：0.774/1.295/−0.083）指向目标点 (10.35,0,−0.2) 的斜下弹道；
            // 近弧 = 5.85 − 烘焙半径 0.32 ≈ 5.535，命中点高 ≈0.65。本地坐标经 InverseTransformPoint 表达。
            var map = NoObstacleMap();
            var (world, sim, view, localView) = BuildBattle(map, new Vector3(5.85f, 0f, -0.2f));
            BuildMount(localView, new Vector3(1f, 1f, 0f));
            Vector3 aimPoint = new Vector3(10.35f, 0f, -0.2f);
            var (input, driver, beam) = BuildDriver(view, sim, map, () => aimPoint);
            world.Entities[0].Flags |= EntityFlags.Aiming;

            SampleInput(input);
            driver.Tick();

            Vector3 p1 = beam.GetPosition(1);
            Assert.AreEqual(-0.859f, p1.x, 1e-3f, "被玩家截停（挂载点本地 x）");
            Assert.AreEqual(-0.349f, p1.y, 1e-3f, "命中点高度 ≈0.65（挂载点本地 y）");
            Assert.AreEqual(5.535f, p1.z, 1e-3f, "截停在玩家近弧 5.6−R（R=烘焙半径 0.32）");
            driver.Dispose();
        }

        [Test]
        [Category(TestCategory.Unit)]
        public void 激光_瞄准点超射程_按弹程截断()
        {
            // 瞄准点 (200.35, 0, −0.2) 超射程 100 ⇒ 弹道被射程截断（打不到的地方束到不了）。
            // 枪口（烘焙值）指向该点，方向不变；沿其走 100m ⇒ 世界端点约 (100.772, 0.65, −0.14)。
            var map = NoObstacleMap();
            var (world, sim, view, localView) = BuildBattle(map);
            BuildMount(localView, new Vector3(1f, 1f, 0f));
            Vector3 aimPoint = new Vector3(200.35f, 0f, -0.2f);
            var (input, driver, beam) = BuildDriver(view, sim, map, () => aimPoint);
            world.Entities[0].Flags |= EntityFlags.Aiming;

            SampleInput(input);
            driver.Tick();

            Vector3 p1 = beam.GetPosition(1);
            Assert.AreEqual(-0.858f, p1.x, 1e-2f, "超射程（挂载点本地 x）");
            Assert.AreEqual(-0.354f, p1.y, 1e-2f, "射程截断点高度（烘焙枪口高 1.295 − 射程内降幅）");
            Assert.AreEqual(100.772f, p1.z, 1e-2f, "按弹程 100m 截断（不到瞄准点）");
            driver.Dispose();
        }

        [Test]
        [Category(TestCategory.Unit)]
        public void 激光_语境内无瞄准点事实_落回枪管模式()
        {
            // 瞄准点注入缺位（相机未就绪/退化帧）——方向口径回退已随单口径退役（《固定斜视角射击方案专项设计》§6）：
            // 无点即无准心事实，落回枪管模式（不猜方向）。
            var map = NoObstacleMap();
            var (world, sim, view, localView) = BuildBattle(map, new Vector3(10f, 0f, -0.2f));
            BuildMount(localView, new Vector3(1f, 1f, 0f));
            var (input, driver, beam) = BuildDriver(view, sim, map);   // 不注入瞄准点
            world.Entities[0].Flags |= EntityFlags.Aiming;

            SampleInput(input);
            driver.Tick();

            // 枪管模式：沿挂载点前向（世界 +X）从挂载点世界位 (0,1,−1) 出射——z=−1 恒定，
            // bot（z=−0.2）垂距 0.8 > 半径 ⇒ 不命中 ⇒ 到射程。
            Assert.AreEqual(new Vector3(0f, 0f, CombatConfig.HitscanRange), beam.GetPosition(1),
                "无瞄准点事实 → 枪管模式（不猜方向）");
            driver.Dispose();
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
            Assert.AreEqual(new Vector3(0f, 0f, CombatConfig.HitscanRange), beam.GetPosition(1), "本地 +Z × 射程");
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
            Assert.AreEqual(new Vector3(0f, 0f, 10f - CombatConfig.HitscanRadius), beam.GetPosition(1));
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
            Assert.AreEqual(new Vector3(0f, 0f, 4.5f), beam.GetPosition(1));
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
            Assert.AreEqual(new Vector3(0f, 0f, 10f - CombatConfig.HitscanRadius), beam.GetPosition(1));
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
            Assert.AreEqual(new Vector3(0f, 0f, 10f - CombatConfig.HitscanRadius), beamGo.GetComponent<LineRenderer>().GetPosition(1),
                "枪口朝 +X → 截停 +X 侧目标");
            driver.Dispose();

            BuildMount(localView, new Vector3(0f, 1f, 0f), worldYawDeg: -90f);     // 转锚点即转向 −X
            var beamGo2 = Scope.CreateGameObject("Beam", typeof(LineRenderer));
            var driver2 = new BattleLaserDriver(view, null, sim, map, beamGo2.GetComponent<LineRenderer>());

            driver2.Tick();
            Assert.AreEqual(new Vector3(0f, 0f, 10f - CombatConfig.HitscanRadius), beamGo2.GetComponent<LineRenderer>().GetPosition(1),
                "枪口朝 −X → 截停 −X 侧目标（端点随挂载点朝向，与鼠标无关）");
            driver2.Dispose();
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 激光_瞄准语境端点收敛到开火射线终点_落在准心线上()
        {
            // 射击语境（瞄准位）内：端点 = **逻辑枪口**（本体+朝向系偏移，与 ShootingSystem 同源）出发的
            // 开火射线截停点（子弹停点）——与枪管方向解耦。挂载点前向取 +Z（与瞄准方向 +X 不同轴）——两模式判然可分。
            var map = NoObstacleMap();
            var (world, sim, view, localView) = BuildBattle(map, new Vector3(10f, 0f, -0.2f));  // 目标在射线上
            BuildMount(localView, new Vector3(1f, 1f, 0f));       // 挂载点世界 yaw=90（本地 +Z = 枪管轴）
            var (input, driver, beam) = BuildDriver(view, sim, map, () => new Vector3(10f, 1f, -0.2f)); // 瞄准点（准心同源）
            world.Entities[0].Flags |= EntityFlags.Aiming;                     // 射击语境（瞄准位）

            SampleInput(input);
            driver.Tick();

            // 收敛端点 = 与子弹同线：逻辑枪口沿 +X 打 10m 处 bot 的近弧 ⇒ 世界 (9.5,1,−0.2)，
            // 挂载点世界位 (0,1,−1) ⇒ 本地 (−0.8, 0, 9.5)。
            // **两模式仍可分**：走枪管模式时沿 +X 出射，bot 的垂距 |−0.2−(−1)| = 0.8 > 半径 0.5 ⇒ 不命中，
            // 端点会是 (0,0,100) 而非 9.5。
            // **已知未收口（登记）**：本行期望硬编码 −0.804，实测 −0.8057（差 1.7 mm）——属判定几何
            // 常量重校后期望未跟的既有面（会话开始时即红，与本次动画/VFX 改动无关）；权威推导式待定后再改。
            AssertLocal(beam.GetPosition(1), new Vector3(-0.804f, 0.01f, 10f - CombatConfig.HitscanRadius), 1e-3f);
            driver.Dispose();
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 激光_开火窗语境同收敛_退出语境回枪管模式()
        {
            var map = NoObstacleMap();
            var (world, sim, view, localView) = BuildBattle(map, new Vector3(10f, 0f, -0.2f));
            BuildMount(localView, new Vector3(1f, 1f, 0f));       // 挂载点世界 yaw=90（本地 +Z = 枪管轴）
            var (input, driver, beam) = BuildDriver(view, sim, map, () => new Vector3(10f, 1f, -0.2f)); // 瞄准点（准心同源）

            // 开火驻留窗（无瞄准位）——同语境口径收敛
            world.Entities[0].FireStanceFrames = (byte)CombatConfig.FireStanceFrames;
            SampleInput(input);
            driver.Tick();
            // 同上例：期望硬编码待重校（既有面，非本次改动引入）
            AssertLocal(beam.GetPosition(1), new Vector3(-0.804f, 0.01f, 10f - CombatConfig.HitscanRadius), 1e-3f, "开火窗内 → 收敛端点（子弹停点）");

            // 窗尽（无瞄准、无窗）→ 回枪管模式：沿枪口 +Z（世界 +X）出射——bot 垂距 0.8 不命中，到射程
            world.Entities[0].FireStanceFrames = 0;
            SampleInput(input);
            driver.Tick();
            Assert.AreEqual(new Vector3(0f, 0f, CombatConfig.HitscanRange), beam.GetPosition(1),
                "退出语境 → 枪管模式（沿枪口前向，与瞄准向量无关）");
            driver.Dispose();
        }

        [Test]
        [Category(TestCategory.Unit)]
        public void 激光_语境内瞄准点注入返回空_落回枪管模式()
        {
            var map = NoObstacleMap();
            var (world, sim, view, localView) = BuildBattle(map, new Vector3(10f, 0f, -0.2f));
            BuildMount(localView, new Vector3(1f, 1f, 0f));       // 挂载点世界 yaw=90（本地 +Z = 枪管轴）
            var (input, driver, beam) = BuildDriver(view, sim, map, () => null);   // 注入源在场但本帧无解算点
            world.Entities[0].Flags |= EntityFlags.Aiming;

            SampleInput(input);
            driver.Tick();
            Assert.AreEqual(new Vector3(0f, 0f, CombatConfig.HitscanRange), beam.GetPosition(1),
                "语境内但瞄准点无解算（未采到/被拦）→ 回枪管模式，不猜方向");
            driver.Dispose();
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
            var (input, driver, beam) = BuildDriver(view, sim, map);

            try
            {
                TestModeRuntime.Active = true;           // 测试模式直通——任何武器都支持
                SampleInput(input);
                driver.Tick();
                Assert.IsTrue(beam.gameObject.activeSelf, "测试模式 → 999 也支持");

                TestModeRuntime.Active = false;          // 退出回策略口径
                SampleInput(input);
                driver.Tick();
                Assert.IsFalse(beam.gameObject.activeSelf, "退出测试模式 → 策略集合重新生效");
            }
            finally
            {
                TestModeRuntime.Active = false;           // 静态态复位（勿泄漏进其他用例）
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

        /// <summary>空载荷设备源替身（装配兼容保留——AimPoint 单口径下驱动不读瞄准输入，只读被拦门，此源不承载数据）。</summary>
        private sealed class FixedAimSource : IIntentSource
        {
            public string Name => "fixed-aim";
            public IntentSample Sample(in SimVector3 localPos) => new IntentSample(new SimInputFrame());
        }
    }
}
