using System;
using LiteSim;
using LiteSim.View;
using LiteTesting;
using LiteTesting.Unity;
using NUnit.Framework;
using UnityEngine;
using LiteClient;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 瞄准激光驱动（<see cref="BattleLaserDriver"/>，修订口径：**激光器装在武器上**）的
    /// L2 EditMode 覆盖：束挂到武器挂载点（Weapon_Rifle/Muzzle）本地空间、方向=挂载点前向（**不追鼠标、
    /// 不读瞄准输入**）、端点=挂载点前向上的截停距离（SimRaycast 单源——实体圆柱/障碍/射程取最近）、
    /// 可见性门（未对齐/死亡/被拦/未装备/不支持/**无挂载点**——灰盒无武器即无激光，诚实退化）。
    /// 真 prefab/真枪口的手感对位归 PlayMode/手测。
    /// </summary>
    public sealed class BattleLaserEditModeTests : UnityTestBase
    {
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
            var sim = new RollbackSim(world, map, new[] { new SimInputFrame { EntityId = self } });
            SimView view = new SimView(world, null,
                factory: (loc, parent) => Scope.CreateGameObject(loc),
                recycler: null);
            view.AlignLocal(self);
            view.Tick(1f / 60f);                        // 首帧落位：HasLocalDisplay 置真 + 视图建立
            view.TryGetView(0, out var localView);
            return (world, sim, view, localView);
        }

        /// <summary>武器激光挂载点（本地视图下的 "Muzzle" 锚点；yaw 90° ⇒ 前向 = 世界 +X）。</summary>
        private Transform BuildMount(GameObject localView, Vector3 localPos, float yawDeg = 90f)
        {
            var mountGo = Scope.CreateGameObject("Muzzle");
            mountGo.transform.SetParent(localView.transform, false);
            mountGo.transform.localPosition = localPos;
            mountGo.transform.localRotation = Quaternion.Euler(0f, yawDeg, 0f);
            return mountGo.transform;
        }

        private (InputService Input, BattleLaserDriver Driver, LineRenderer Beam) BuildDriver(
            SimView view, RollbackSim sim, SimMapData map, Func<Vector3?> aimPointOf = null)
        {
            var input = new InputService();
            input.SetSource(new FixedAimSource(1f, 0f));     // 兼装配：瞄准点注入可用时端点走准心点，此值仅回退路径消费
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
            BuildMount(localView, new Vector3(1f, 1f, 0f), yawDeg: 0f);
            Vector3 aimPoint = new Vector3(10.35f, 0f, -0.2f);   // 在枪口射线上（aimDist=10，射程内）
            var (input, driver, beam) = BuildDriver(view, sim, map, () => aimPoint);
            world.Entities[0].Flags |= EntityFlags.Aiming;

            SampleInput(input);
            driver.Tick();

            Vector3 p1 = beam.GetPosition(1);
            Assert.IsTrue(beam.gameObject.activeSelf, "支持武器 + 挂载点 + 瞄准点 → 显示");
            Assert.AreEqual(9.35f, p1.x, 1e-4f, "端点=准心地面点（挂载点本地：10.35−1）");
            Assert.AreEqual(-1f, p1.y, 1e-4f, "束落到地面（0−1）");
            Assert.AreEqual(-0.2f, p1.z, 1e-4f);
            driver.Dispose();
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 激光_瞄准点被玩家挡路_端点按截停比例落束段()
        {
            // 挡在枪口→准心点之间：水平截停 t=5（bot 近弧 5.5−0.5），aimDist=10 ⇒ f=0.5——
            // 端点 = Lerp(挂载点(1,1,0), 准心点(10.35,0,−0.2), 0.5) = (5.675, 0.5, −0.1)
            var map = NoObstacleMap();
            var (world, sim, view, localView) = BuildBattle(map, new Vector3(5.85f, 0f, -0.2f));
            BuildMount(localView, new Vector3(1f, 1f, 0f), yawDeg: 0f);
            Vector3 aimPoint = new Vector3(10.35f, 0f, -0.2f);
            var (input, driver, beam) = BuildDriver(view, sim, map, () => aimPoint);
            world.Entities[0].Flags |= EntityFlags.Aiming;

            SampleInput(input);
            driver.Tick();

            Vector3 p1 = beam.GetPosition(1);
            Assert.AreEqual(5.675f, p1.x, 1e-3f, "被玩家截停——端点落在束段一半处");
            Assert.AreEqual(0.5f, p1.y, 1e-3f);
            Assert.AreEqual(-0.1f, p1.z, 1e-3f);
            driver.Dispose();
        }

        [Test]
        [Category(TestCategory.Unit)]
        public void 激光_瞄准点超射程_按弹程截断()
        {
            // aimDist=200 > 射程 100 ⇒ maxT=100、无遮挡 t=100 ⇒ f=0.5——端点只到束段一半（打不到的地方束到不了）
            var map = NoObstacleMap();
            var (world, sim, view, localView) = BuildBattle(map);
            BuildMount(localView, new Vector3(1f, 1f, 0f), yawDeg: 0f);
            Vector3 aimPoint = new Vector3(200.35f, 0f, -0.2f);
            var (input, driver, beam) = BuildDriver(view, sim, map, () => aimPoint);
            world.Entities[0].Flags |= EntityFlags.Aiming;

            SampleInput(input);
            driver.Tick();

            Vector3 p1 = beam.GetPosition(1);
            Assert.AreEqual(100.675f, p1.x, 1e-2f, "超射程——按弹程比例截断（不到准心点）");
            Assert.AreEqual(0.5f, p1.y, 1e-3f);
            Assert.AreEqual(-0.1f, p1.z, 1e-3f);
            driver.Dispose();
        }

        [Test]
        [Category(TestCategory.Unit)]
        public void 激光_语境内无瞄准点事实_回退开火射线终点()
        {
            // 瞄准点注入缺位（无鼠标设备/未采到）——回退瞄准向量版（与子弹同线）；本用例即回退路径锁
            var map = NoObstacleMap();
            var (world, sim, view, localView) = BuildBattle(map, new Vector3(10f, 0f, -0.2f));
            BuildMount(localView, new Vector3(1f, 1f, 0f), yawDeg: 0f);
            var (input, driver, beam) = BuildDriver(view, sim, map);   // 不注入瞄准点
            world.Entities[0].Flags |= EntityFlags.Aiming;

            SampleInput(input);                                      // 瞄准向量 +X（FixedAimSource）
            driver.Tick();

            Assert.AreEqual(new Vector3(8.5f, 0f, -0.2f), beam.GetPosition(1), "回退：逻辑枪口 + 瞄准向量终点");
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
            Assert.AreEqual(new Vector3(0f, 0f, 9.5f), beam.GetPosition(1));
            driver.Dispose();
        }

        [Test]
        [Category(TestCategory.Unit)]
        public void 激光_方向随枪不追鼠标_无输入也出束()
        {
            // 两侧各一个目标；枪口（挂载点）转向谁就打谁——端点与瞄准输入无关
            var map = NoObstacleMap();
            var (world, sim, view, localView) = BuildBattle(map,
                new Vector3(10f, 0f, 0f), new Vector3(-10f, 0f, 0f));
            Transform mount = BuildMount(localView, new Vector3(0f, 1f, 0f), yawDeg: 90f);   // 前向 +X
            var beamGo = Scope.CreateGameObject("Beam", typeof(LineRenderer));
            var driver = new BattleLaserDriver(view, null, sim, map, beamGo.GetComponent<LineRenderer>());
            _ = world;

            driver.Tick();                                // 无输入服务：不拦、不读瞄准——照样出束
            Assert.IsTrue(beamGo.activeSelf, "无输入形态也显示（方向来自枪，不来自输入/鼠标）");
            Assert.AreEqual(new Vector3(0f, 0f, 9.5f), beamGo.GetComponent<LineRenderer>().GetPosition(1),
                "枪口朝 +X → 截停 +X 侧目标");

            mount.localRotation = Quaternion.Euler(0f, -90f, 0f);    // 枪口转向 −X（美术转锚点即转向）
            driver.Tick();
            Assert.AreEqual(new Vector3(0f, 0f, 9.5f), beamGo.GetComponent<LineRenderer>().GetPosition(1),
                "枪口朝 −X → 截停 −X 侧目标（端点随挂载点转向，与鼠标无关）");
            driver.Dispose();
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 激光_瞄准语境端点收敛到开火射线终点_落在准心线上()
        {
            // 射击语境（瞄准位）内：端点 = **逻辑枪口**（本体+朝向系偏移，与 ShootingSystem 同源）出发的
            // 开火射线截停点（子弹停点）——与枪管方向解耦。挂载点前向取 +Z（与瞄准方向 +X 不同轴）——两模式判然可分。
            var map = NoObstacleMap();
            var (world, sim, view, localView) = BuildBattle(map, new Vector3(10f, 0f, -0.2f));  // 目标在偏移射线上
            BuildMount(localView, new Vector3(1f, 1f, 0f), yawDeg: 0f);       // 前向 +Z（枪口指向 z 轴）
            var (input, driver, beam) = BuildDriver(view, sim, map);
            world.Entities[0].Flags |= EntityFlags.Aiming;                     // 射击语境（瞄准位）

            SampleInput(input);                                                // 瞄准向量 +X（FixedAimSource）
            driver.Tick();

            // 收敛端点：逻辑枪口 (0.35,1,-0.2) +X → 目标圆柱近弧 t=10-0.35-0.5=9.15 ⇒ 世界 (9.5,1,-0.2)；
            // 挂载点本地（位 (1,1,0)、identity 转向）= (8.5, 0, -0.2)——若走枪管模式会是 (0,0,100)（+Z 无遮挡）
            Assert.AreEqual(new Vector3(8.5f, 0f, -0.2f), beam.GetPosition(1));
            driver.Dispose();
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 激光_开火窗语境同收敛_退出语境回枪管模式()
        {
            var map = NoObstacleMap();
            var (world, sim, view, localView) = BuildBattle(map, new Vector3(10f, 0f, -0.2f));
            BuildMount(localView, new Vector3(1f, 1f, 0f), yawDeg: 0f);       // 前向 +Z
            var (input, driver, beam) = BuildDriver(view, sim, map);

            // 开火驻留窗（无瞄准位）——同语境口径收敛
            world.Entities[0].FireStanceFrames = (byte)CombatConfig.FireStanceFrames;
            SampleInput(input);
            driver.Tick();
            Assert.AreEqual(new Vector3(8.5f, 0f, -0.2f), beam.GetPosition(1), "开火窗内 → 收敛端点（子弹停点）");

            // 窗尽（无瞄准、无窗）→ 回枪管模式：沿枪口 +Z 出射——无遮挡到射程
            world.Entities[0].FireStanceFrames = 0;
            SampleInput(input);
            driver.Tick();
            Assert.AreEqual(new Vector3(0f, 0f, CombatConfig.HitscanRange), beam.GetPosition(1),
                "退出语境 → 枪管模式（沿枪口前向，与瞄准向量无关）");
            driver.Dispose();
        }

        [Test]
        [Category(TestCategory.Unit)]
        public void 激光_语境内零瞄准向量_落回枪管模式()
        {
            var map = NoObstacleMap();
            var (world, sim, view, localView) = BuildBattle(map, new Vector3(10f, 0f, -0.2f));
            BuildMount(localView, new Vector3(1f, 1f, 0f), yawDeg: 0f);       // 前向 +Z
            var (input, driver, beam) = BuildDriver(view, sim, map);
            input.SetSource(new FixedAimSource(0f, 0f));                      // 全零瞄准向量（被拦清零同款）
            world.Entities[0].Flags |= EntityFlags.Aiming;

            SampleInput(input);
            driver.Tick();
            Assert.AreEqual(new Vector3(0f, 0f, CombatConfig.HitscanRange), beam.GetPosition(1),
                "语境内但无瞄准向量（未采到/被拦）→ 回枪管模式，不猜方向");
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
            var sim = new RollbackSim(world, map, new[] { new SimInputFrame { EntityId = self } });
            SimView view = new SimView(world, null,
                factory: (loc, parent) => Scope.CreateGameObject(loc),
                recycler: null);                           // 未 AlignLocal：LocalEntityId=0、HasLocalDisplay=false
            var (input, driver, beam) = BuildDriver(view, sim, map);

            SampleInput(input);
            driver.Tick();
            Assert.IsFalse(beam.gameObject.activeSelf, "本地表现未建 → 隐藏");
            driver.Dispose();
        }

        /// <summary>固定 Aim 的设备源替身（装配兼容保留——修订口径下驱动不读 Aim，此源不应影响结果）。</summary>
        private sealed class FixedAimSource : IIntentSource
        {
            private readonly SimInputFrame _frame;
            public FixedAimSource(float ax, float az) => _frame = new SimInputFrame { AimX = ax, AimZ = az };
            public string Name => "fixed-aim";
            public IntentSample Sample(in SimVector3 localPos) => new IntentSample(_frame);
        }
    }
}
