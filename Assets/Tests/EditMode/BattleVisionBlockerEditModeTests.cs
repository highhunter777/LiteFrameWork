using System;
using LiteSim;
using LiteView;
using LiteTesting;
using LiteTesting.Unity;
using NUnit.Framework;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 视野遮蔽驱动（<see cref="BattleVisionBlockerDriver"/>——档 A1 复合方案·姿态动态）的
    /// L2 EditMode 覆盖：**渐隐面**（相机→本地柱顶线段被高墙挡 ⇒ <c>sharedMaterial</c> 切渐隐
    /// 变体；站姿柱顶越过 1m 低箱 ⇒ 不渐隐——真值表"站姿 @ 1m 箱"行）、**影区面**（渐隐在场时
    /// 墙后实体隐藏、露头实体照常、本地玩家豁免、无渐隐不隐藏）、**迟滞**（连续两拍同向才切换）、
    /// **保守恢复**（无相机/未对齐/拆件全归位）、**内容配对**（材质不成对零绑定——全惰性）。
    ///
    /// **夹具口径**：材质对用**真实资产**（<c>AssetDatabase</c> 只读载入，驱动只改渲染件引用、
    /// 不改材质本体——断言用引用相等）；场景侧用合成 <see cref="MapTopology.MapCollisionRoot"/>
    /// 代理树（Cube 原语＝BoxCollider＋渲染件，与烘焙器 Box 采集同构）；视图走 <see cref="SimView"/>
    /// 合成世界（同 BattleLaserEditModeTests 形态）。判定几何本身有 L1 权威守卫
    /// （MapSceneReconciliation/AimParallaxHitTests），本组只守 View 层"谁渐隐/谁隐藏"的映射。
    /// </summary>
    public sealed class BattleVisionBlockerEditModeTests : UnityTestBase
    {
        /// <summary>掩体材质对（与 BattleViewRig 取件常量同串——真资产对，测试只读引用）。</summary>
        private const string BaseMaterialPath = "Assets/Scenes/Arena01/Arena01_Cover.mat";
        private const string FadeMaterialPath = "Assets/Scenes/Arena01/Arena01_Cover_Fade.mat";

        /// <summary>探测节拍与迟滞拍数（驱动私有常量同值——驱动面契约，改驱动须同步本组步进）。</summary>
        private const float ProbeSeconds = 0.1f;
        private const int HoldProbes = 2;

        /// <summary>渐隐在场到实体隐藏收敛所需的总拍数（渐隐两拍＋隐藏两拍，取安全余量）。</summary>
        private const int SettledProbes = HoldProbes * 2 + 1;

        private static (Material Base, Material Fade) LoadMaterialPair()
        {
            Material baseMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(BaseMaterialPath);
            Material fadeMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(FadeMaterialPath);
            Assert.IsNotNull(baseMat, $"缺基准材质资产:{BaseMaterialPath}");
            Assert.IsNotNull(fadeMat, $"缺渐隐变体资产:{FadeMaterialPath}");
            Assert.AreNotSame(baseMat, fadeMat, "基准与渐隐必须是两个资产（成对裁决）");
            return (baseMat, fadeMat);
        }

        /// <summary>
        /// 合成世界与视图：本地在 <paramref name="localPos"/>，远端逐位 <paramref name="remotePositions"/>
        /// （视图件带 MeshRenderer——影区隐藏面的断言对象）。返回远端视图数组与槽位顺序一致。
        /// </summary>
        private (SimWorldState World, SimView View, GameObject LocalView, GameObject[] RemoteViews) BuildWorld(
            Vector3 localPos, params Vector3[] remotePositions)
        {
            var world = new SimWorldState { RngState = 1UL };
            long self = world.Spawn(new EntitySlot { Hp = 100, Pos = ToSim(localPos) }, out int localSlot);
            var remoteSlots = new int[remotePositions.Length];
            for (int i = 0; i < remotePositions.Length; i++)
                world.Spawn(new EntitySlot { Hp = 100, Pos = ToSim(remotePositions[i]) }, out remoteSlots[i]);

            SimView view = new SimView(world, null,
                factory: (loc, parent) => Scope.CreateGameObject(loc, typeof(MeshRenderer)),
                recycler: null);
            view.AlignLocal(self);
            view.Tick(1f / 60f);                                   // 首帧落位 + 视图建立

            view.TryGetView(localSlot, out GameObject localView);
            var remoteViews = new GameObject[remotePositions.Length];
            for (int i = 0; i < remoteSlots.Length; i++)
                view.TryGetView(remoteSlots[i], out remoteViews[i]);
            return (world, view, localView, remoteViews);
        }

        /// <summary>合成渲染相机（挂原点，位姿由调用方摆——驱动只读其位置）。</summary>
        private (GameObject Go, Func<Camera> CameraOf) BuildCamera(Vector3 eye)
        {
            GameObject go = Scope.CreateGameObject("Cam", typeof(Camera));
            go.transform.position = eye;
            return (go, () => go.GetComponent<Camera>());
        }

        /// <summary>
        /// 合成 <see cref="MapTopology.MapCollisionRoot"/> 代理树＋单盒件（Cube 原语＝BoxCollider＋渲染件，
        /// 世界位＝(x, 高/2, z)、缩放＝(2·半宽, 高, 2·半深)——与烘焙器 Box 采集同构：bounds.center 的
        /// XZ 即障碍 Center 的 XZ），并返回镜像的判定图。材质决定绑定面（成对基准 ⇒ 可渐隐；
        /// 其他 ⇒ 零绑定）。
        /// </summary>
        private (SimMapData Map, Renderer Renderer) BindBoxObstacle(
            float x, float z, float halfX, float halfZ, float height, Material material)
        {
            GameObject root = Scope.CreateGameObject(MapTopology.MapCollisionRoot);
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "box_proxy";
            box.transform.SetParent(root.transform);
            box.transform.position = new Vector3(x, height * 0.5f, z);
            box.transform.localScale = new Vector3(halfX * 2f, height, halfZ * 2f);
            Renderer renderer = box.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            Physics.SyncTransforms();                              // EditMode 手摆位形后同步碰撞包围盒

            var map = new SimMapData
            {
                ObstacleCount = 1,
                GroundY = 0f,
                HalfWidth = 100f,
                HalfDepth = 100f,
            };
            map.Obstacles[0] = new SimObstacle
            {
                Kind = SimObstacleKind.Box,
                Center = new SimVector3(x, 0f, z),
                HalfX = halfX,
                HalfZ = halfZ,
                Height = height,
            };
            return (map, renderer);
        }

        private BattleVisionBlockerDriver BuildDriver(SimView view, SimMapData map, Func<Camera> cameraOf,
            Material baseMat, Material fadeMat)
            => new BattleVisionBlockerDriver(view, map, cameraOf, baseMat, fadeMat,
                go => go.GetComponentsInChildren<Renderer>(true));

        private static SimVector3 ToSim(Vector3 p) => new SimVector3(p.x, p.y, p.z);

        private static MeshRenderer RendererOf(GameObject view) => view.GetComponent<MeshRenderer>();

        // ---- 几何单测（VisionSegment——真三维线段，与 SimRaycast 2.5D 弹道口径分立）----

        [Test]
        [Category(TestCategory.Unit)]
        public void 视线线段_高墙挡俯冲线段_低箱被站姿柱顶越过()
        {
            // 相机 (0,10,−10) → 本地柱顶 (0,1.8,0)：45° 俯冲线段。
            // 同一足迹两档障碍：20m 高墙在 z=−4 处线段高度 ≈5.1m < 20 ⇒ 挡；
            // 1m 低箱在 z=−4 处线段高度 ≈5.1m > 1 ⇒ 越过（真值表"站姿 @ 1m 箱＝露头带"行）。
            Vector3 eye = new Vector3(0f, 10f, -10f);
            Vector3 top = new Vector3(0f, BodyBake.Height, 0f);

            SimObstacle wall = new SimObstacle
            {
                Kind = SimObstacleKind.Box, Center = new SimVector3(0f, 0f, -4f),
                HalfX = 3f, HalfZ = 0.5f, Height = 20f,
            };
            SimObstacle lowBox = wall;
            lowBox.Height = 1f;

            Assert.IsTrue(VisionSegment.Blocked(in wall, eye, top), "高墙挡俯冲线段");
            Assert.IsFalse(VisionSegment.Blocked(in lowBox, eye, top), "站姿柱顶越过 1m 低箱");
        }

        [Test]
        [Category(TestCategory.Unit)]
        public void 视线线段_圆柱障碍高低两档同真值表()
        {
            Vector3 eye = new Vector3(0f, 10f, -10f);
            Vector3 top = new Vector3(0f, BodyBake.Height, 0f);

            SimObstacle pillar = new SimObstacle
            {
                Kind = SimObstacleKind.Circle, Center = new SimVector3(0f, 0f, -4f),
                Radius = 2f, Height = 20f,
            };
            Assert.IsTrue(VisionSegment.Blocked(in pillar, eye, top), "20m 圆柱挡俯冲线段");

            pillar.Height = 1f;
            Assert.IsFalse(VisionSegment.Blocked(in pillar, eye, top), "1m 圆柱被站姿柱顶越过（XZ 进入时线段高度 > 1）");
        }

        [Test]
        [Category(TestCategory.Unit)]
        public void 视线线段_垂直与水平线段的退化口径()
        {
            // 垂直线段（相机压在柱顶正上方）：XZ 恒定 ⇒ 垂距在半径内且 y 区间相交即挡
            SimObstacle pillar = new SimObstacle
            {
                Kind = SimObstacleKind.Circle, Center = new SimVector3(0f, 0f, 0f),
                Radius = 2f, Height = 5f,
            };
            Assert.IsTrue(VisionSegment.Blocked(in pillar, new Vector3(0f, 10f, 0f), new Vector3(0f, 1f, 0f)),
                "垂直线段穿圆柱 y 带");
            Assert.IsFalse(VisionSegment.Blocked(in pillar, new Vector3(5f, 10f, 0f), new Vector3(5f, 1f, 0f)),
                "垂直线段在圆柱外（垂距 5 > 半径 2）");

            // 水平线段（uy=0）：沿用"原点高度在带内"闸——带内横穿挡、带上方越过不挡
            SimObstacle wall = new SimObstacle
            {
                Kind = SimObstacleKind.Box, Center = new SimVector3(0f, 0f, -4f),
                HalfX = 3f, HalfZ = 0.5f, Height = 20f,
            };
            Assert.IsTrue(VisionSegment.Blocked(in wall, new Vector3(-10f, 5f, -4f), new Vector3(10f, 5f, -4f)),
                "水平线段在带内横穿墙体 XZ");
            Assert.IsFalse(VisionSegment.Blocked(in wall, new Vector3(-10f, 25f, -4f), new Vector3(10f, 25f, -4f)),
                "水平线段在墙顶上方（原点高度在带外）");
        }

        [Test]
        [Category(TestCategory.Unit)]
        public void 视线线段_起点在障碍内算挡_空图不挡()
        {
            SimObstacle wall = new SimObstacle
            {
                Kind = SimObstacleKind.Box, Center = new SimVector3(0f, 0f, -4f),
                HalfX = 3f, HalfZ = 0.5f, Height = 20f,
            };
            Assert.IsTrue(VisionSegment.Blocked(in wall, new Vector3(0f, 5f, -4f), new Vector3(0f, 5f, 10f)),
                "起点在墙内 → t=0 命中（与 SimRaycast 同口径）");

            var empty = new SimMapData();
            Assert.IsFalse(VisionSegment.BlockedByAny(in empty, Vector3.zero, new Vector3(0f, 10f, 0f)),
                "空图恒不挡");
        }

        // ---- 驱动行为（渐隐面）----

        [Test]
        [Category(TestCategory.Contract)]
        public void 遮蔽_高墙挡本地柱顶_切渐隐变体_清空遮挡后恢复()
        {
            var (baseMat, fadeMat) = LoadMaterialPair();
            (SimMapData map, Renderer wall) = BindBoxObstacle(0f, -4f, 3f, 0.5f, 20f, baseMat);
            var (_, view, _, _) = BuildWorld(Vector3.zero, new Vector3(0f, 0f, 20f));
            (GameObject camGo, Func<Camera> cameraOf) = BuildCamera(new Vector3(0f, 10f, -10f));
            BattleVisionBlockerDriver driver = BuildDriver(view, map, cameraOf, baseMat, fadeMat);
            try
            {
                for (int i = 0; i < HoldProbes; i++) driver.Tick(ProbeSeconds);
                Assert.AreSame(fadeMat, wall.sharedMaterial, "墙挡相机→本地柱顶线段 ⇒ 渐隐变体");

                // 相机移到墙侧（线段不再穿墙）——迟滞后恢复基准材质
                camGo.transform.position = new Vector3(20f, 10f, -10f);
                driver.Tick(ProbeSeconds);
                Assert.AreSame(fadeMat, wall.sharedMaterial, "清空后单拍不恢复（迟滞）");
                driver.Tick(ProbeSeconds);
                Assert.AreSame(baseMat, wall.sharedMaterial, "连续两拍清空 ⇒ 恢复基准材质");
            }
            finally
            {
                driver.Dispose();
            }
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 遮蔽_站姿柱顶越过一米低箱_不渐隐()
        {
            var (baseMat, fadeMat) = LoadMaterialPair();
            (SimMapData map, Renderer box) = BindBoxObstacle(0f, -4f, 3f, 0.5f, 1f, baseMat);
            var (_, view, _, _) = BuildWorld(Vector3.zero, new Vector3(0f, 0f, 20f));
            (_, Func<Camera> cameraOf) = BuildCamera(new Vector3(0f, 10f, -10f));
            BattleVisionBlockerDriver driver = BuildDriver(view, map, cameraOf, baseMat, fadeMat);
            try
            {
                for (int i = 0; i < SettledProbes; i++) driver.Tick(ProbeSeconds);
                Assert.AreSame(baseMat, box.sharedMaterial, "站姿 1m 箱＝露头带——自然部分遮挡，不渐隐");
            }
            finally
            {
                driver.Dispose();
            }
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 遮蔽_迟滞_单拍遮挡不切换()
        {
            var (baseMat, fadeMat) = LoadMaterialPair();
            (SimMapData map, Renderer wall) = BindBoxObstacle(0f, -4f, 3f, 0.5f, 20f, baseMat);
            var (_, view, _, _) = BuildWorld(Vector3.zero, new Vector3(0f, 0f, 20f));
            (_, Func<Camera> cameraOf) = BuildCamera(new Vector3(0f, 10f, -10f));
            BattleVisionBlockerDriver driver = BuildDriver(view, map, cameraOf, baseMat, fadeMat);
            try
            {
                driver.Tick(ProbeSeconds);
                Assert.AreSame(baseMat, wall.sharedMaterial, "单拍遮挡不切换（防贴边闪烁）");
                driver.Tick(ProbeSeconds);
                Assert.AreSame(fadeMat, wall.sharedMaterial, "连续两拍 ⇒ 切换");
            }
            finally
            {
                driver.Dispose();
            }
        }

        // ---- 驱动行为（影区实体隐藏面）----

        [Test]
        [Category(TestCategory.Contract)]
        public void 遮蔽_渐隐在场_墙后实体隐藏_露头实体照常_本地豁免()
        {
            var (baseMat, fadeMat) = LoadMaterialPair();
            (SimMapData map, Renderer _) = BindBoxObstacle(0f, -4f, 3f, 0.5f, 20f, baseMat);
            // 远端两枚：墙后 (0,0,6)——相机→其柱顶穿墙；露头 (10,0,6)——柱顶线段从墙侧越过
            var (_, view, localView, remoteViews) = BuildWorld(Vector3.zero,
                new Vector3(0f, 0f, 6f), new Vector3(10f, 0f, 6f));
            (_, Func<Camera> cameraOf) = BuildCamera(new Vector3(0f, 10f, -10f));
            BattleVisionBlockerDriver driver = BuildDriver(view, map, cameraOf, baseMat, fadeMat);
            try
            {
                for (int i = 0; i < SettledProbes; i++) driver.Tick(ProbeSeconds);

                Assert.IsFalse(RendererOf(remoteViews[0]).enabled, "墙后实体（柱顶被墙挡）隐藏——掩体后的视野不可见");
                Assert.IsTrue(RendererOf(remoteViews[1]).enabled, "露头实体（柱顶未被挡）照常显示");
                Assert.IsTrue(RendererOf(localView).enabled, "本地玩家豁免——渐隐只为找回自己");
            }
            finally
            {
                driver.Dispose();
            }
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 遮蔽_材质不成对_零绑定_渐隐与隐藏全惰性()
        {
            // 墙体渲染件挂非基准材质（内建默认材质——内容配对裁决：不成对即不渐隐）⇒ 零绑定 ⇒
            // 不渐隐 ⇒ 影区面惰性——非渐隐遮挡由深度测试天然兜底，现状即安全。
            var (baseMat, fadeMat) = LoadMaterialPair();
            GameObject probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Scope.Track(probe);                                     // 原语件（未挂 Scope 树）显式托管
            Material unpairedMat = probe.GetComponent<Renderer>().sharedMaterial;   // 内建默认材质（持久资产，勿 Track 勿销毁）
            (SimMapData map, Renderer wall) = BindBoxObstacle(0f, -4f, 3f, 0.5f, 20f, unpairedMat);
            var (_, view, _, remoteViews) = BuildWorld(Vector3.zero, new Vector3(0f, 0f, 6f));
            (_, Func<Camera> cameraOf) = BuildCamera(new Vector3(0f, 10f, -10f));
            BattleVisionBlockerDriver driver = BuildDriver(view, map, cameraOf, baseMat, fadeMat);
            try
            {
                for (int i = 0; i < SettledProbes; i++) driver.Tick(ProbeSeconds);
                Assert.AreSame(unpairedMat, wall.sharedMaterial, "材质不成对不入绑定——材质永不动");
                Assert.IsTrue(RendererOf(remoteViews[0]).enabled, "无渐隐在场 ⇒ 不做实体隐藏");
            }
            finally
            {
                driver.Dispose();
            }
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 遮蔽_切出影区_实体即时恢复_材质同步归位()
        {
            var (baseMat, fadeMat) = LoadMaterialPair();
            (SimMapData map, Renderer wall) = BindBoxObstacle(0f, -4f, 3f, 0.5f, 20f, baseMat);
            var (_, view, _, remoteViews) = BuildWorld(Vector3.zero, new Vector3(0f, 0f, 6f));
            (GameObject camGo, Func<Camera> cameraOf) = BuildCamera(new Vector3(0f, 10f, -10f));
            BattleVisionBlockerDriver driver = BuildDriver(view, map, cameraOf, baseMat, fadeMat);
            try
            {
                for (int i = 0; i < SettledProbes; i++) driver.Tick(ProbeSeconds);
                Assert.IsFalse(RendererOf(remoteViews[0]).enabled, "渐隐在场 ⇒ 墙后实体隐藏");

                camGo.transform.position = new Vector3(20f, 10f, -10f);
                for (int i = 0; i < HoldProbes; i++) driver.Tick(ProbeSeconds);
                Assert.IsTrue(RendererOf(remoteViews[0]).enabled, "切出影区 ⇒ 渲染开关恢复（不销毁）");
                Assert.AreSame(baseMat, wall.sharedMaterial, "遮挡清空 ⇒ 材质归位");
            }
            finally
            {
                driver.Dispose();
            }
        }

        // ---- 保守面 ----

        [Test]
        [Category(TestCategory.Contract)]
        public void 遮蔽_无相机_保守不渐隐且在场渐隐即恢复()
        {
            var (baseMat, fadeMat) = LoadMaterialPair();
            (SimMapData map, Renderer wall) = BindBoxObstacle(0f, -4f, 3f, 0.5f, 20f, baseMat);
            var (_, view, _, remoteViews) = BuildWorld(Vector3.zero, new Vector3(0f, 0f, 6f));
            (GameObject camGo, _) = BuildCamera(new Vector3(0f, 10f, -10f));
            bool cameraAlive = true;
            Func<Camera> cameraOf = () => cameraAlive ? camGo.GetComponent<Camera>() : null;
            BattleVisionBlockerDriver driver = BuildDriver(view, map, cameraOf, baseMat, fadeMat);
            try
            {
                for (int i = 0; i < SettledProbes; i++) driver.Tick(ProbeSeconds);
                Assert.AreSame(fadeMat, wall.sharedMaterial, "相机在场 ⇒ 渐隐正常工作");

                cameraAlive = false;                                // 相机解析失败（服务缺口/替身形态）
                driver.Tick(ProbeSeconds);
                Assert.AreSame(baseMat, wall.sharedMaterial, "无相机 ⇒ 保守恢复（不留残态）");
                Assert.IsTrue(RendererOf(remoteViews[0]).enabled, "无相机 ⇒ 隐藏同步恢复");
            }
            finally
            {
                driver.Dispose();
            }
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 遮蔽_本地未对齐_不渐隐()
        {
            var (baseMat, fadeMat) = LoadMaterialPair();
            (SimMapData map, Renderer wall) = BindBoxObstacle(0f, -4f, 3f, 0.5f, 20f, baseMat);
            var world = new SimWorldState { RngState = 1UL };
            world.Spawn(new EntitySlot { Hp = 100, Pos = ToSim(Vector3.zero) }, out _);
            SimView view = new SimView(world, null,
                factory: (loc, parent) => Scope.CreateGameObject(loc, typeof(MeshRenderer)),
                recycler: null);                                    // 未 AlignLocal：LocalEntityId=0
            view.Tick(1f / 60f);
            (_, Func<Camera> cameraOf) = BuildCamera(new Vector3(0f, 10f, -10f));
            BattleVisionBlockerDriver driver = BuildDriver(view, map, cameraOf, baseMat, fadeMat);
            try
            {
                for (int i = 0; i < SettledProbes; i++) driver.Tick(ProbeSeconds);
                Assert.AreSame(baseMat, wall.sharedMaterial, "未对齐（无本地表现锚点）⇒ 不渐隐");
            }
            finally
            {
                driver.Dispose();
            }
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 遮蔽_拆件_渐隐与隐藏在场全恢复()
        {
            var (baseMat, fadeMat) = LoadMaterialPair();
            (SimMapData map, Renderer wall) = BindBoxObstacle(0f, -4f, 3f, 0.5f, 20f, baseMat);
            var (_, view, _, remoteViews) = BuildWorld(Vector3.zero, new Vector3(0f, 0f, 6f));
            (_, Func<Camera> cameraOf) = BuildCamera(new Vector3(0f, 10f, -10f));
            BattleVisionBlockerDriver driver = BuildDriver(view, map, cameraOf, baseMat, fadeMat);
            for (int i = 0; i < SettledProbes; i++) driver.Tick(ProbeSeconds);
            Assert.AreSame(fadeMat, wall.sharedMaterial, "拆前已渐隐");
            Assert.IsFalse(RendererOf(remoteViews[0]).enabled, "拆前已隐藏");

            driver.Dispose();                                      // 对局收尾在场景卸载前必达的恢复面
            Assert.AreSame(baseMat, wall.sharedMaterial, "拆件 ⇒ 材质归位（不留渐隐残态）");
            Assert.IsTrue(RendererOf(remoteViews[0]).enabled, "拆件 ⇒ 实体归可见");
        }
    }
}
