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
    /// 视野遮蔽驱动（<see cref="BattleVisionBlockerDriver"/>——档 A1·v3.4 角色视觉权威）的
    /// L2 EditMode 覆盖：**显隐层**（本地角色眼位→目标身体多点采样；任一点无遮挡 ⇒ 渲染，
    /// 全挡 ⇒ 隐藏；本地玩家豁免；真单向可见按掩体优势接受，不再要求两端同结论）、**渐隐层**
    /// （相机→本地柱顶 ⇒ 自身渐隐；相机→**角色可见远端**柱顶 ⇒ 远端渐隐；全挡敌人不参与渐隐）、
    /// **迟滞＋最短保持**（连续两拍同向且状态已保持 0.5s 才切换——掩体边缘不闪隐）、
    /// **保守恢复**（无相机/未对齐/拆件全归位）、**内容配对**（材质不成对 ⇒ 只不渐隐，显隐照常）。
    ///
    /// **夹具口径**：材质对用**真实资产**（<c>AssetDatabase</c> 只读载入，驱动只改渲染件引用、
    /// 不改材质本体——断言用引用相等）；场景侧用合成 <see cref="MapTopology.MapCollisionRoot"/>
    /// 代理树（Cube 原语＝BoxCollider＋渲染件，与烘焙器 Box 采集同构）；视图走 <see cref="SimView"/>
    /// 合成世界（同 BattleLaserEditModeTests 形态）。判定几何本身有 L1 权威守卫
    /// （MapSceneReconciliation/AimParallaxHitTests），本组只守 View 层"谁渐隐/谁隐藏"的映射。
    /// **墙体位置约定**：本地在原点、相机在 `z=+12`；近侧远端 `z=+6`（相机与本地之间）、
    /// 远侧远端 `z=+20`（本地另一侧）。同一堵墙对显隐与相机补位两类线段各测各的口径。
    /// </summary>
    public sealed class BattleVisionBlockerEditModeTests : UnityTestBase
    {
        /// <summary>掩体材质对（与 BattleViewRig 取件常量同串——真资产对，测试只读引用）。</summary>
        private const string BaseMaterialPath = "Assets/Scenes/Arena01/Arena01_Cover.mat";
        private const string FadeMaterialPath = "Assets/Scenes/Arena01/Arena01_Cover_Fade.mat";

        /// <summary>探测节拍与迟滞拍数（驱动私有常量同值——驱动面契约，改驱动须同步本组步进）。</summary>
        private const float ProbeSeconds = 0.1f;
        private const int HoldProbes = 2;

        /// <summary>状态收敛所需的总拍数（显隐/渐隐各两拍＋最短保持，取安全余量）。</summary>
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
            (SimMapData map, Renderer wall) = BindBoxObstacle(0f, 3f, 3f, 0.5f, 20f, baseMat);
            var (_, view, _, _) = BuildWorld(Vector3.zero, new Vector3(0f, 0f, 20f));
            (GameObject camGo, Func<Camera> cameraOf) = BuildCamera(new Vector3(0f, 10f, 12f));
            BattleVisionBlockerDriver driver = BuildDriver(view, map, cameraOf, baseMat, fadeMat);
            try
            {
                for (int i = 0; i < HoldProbes; i++) driver.Tick(ProbeSeconds);
                Assert.AreSame(fadeMat, wall.sharedMaterial, "墙挡相机→本地柱顶线段 ⇒ 渐隐变体");

                // 相机移到墙侧（线段不再穿墙）——迟滞后恢复基准材质
                camGo.transform.position = new Vector3(20f, 10f, 12f);
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
        public void 遮蔽_站姿越过一米低箱_显隐照常且不渐隐()
        {
            var (baseMat, fadeMat) = LoadMaterialPair();
            (SimMapData map, Renderer box) = BindBoxObstacle(0f, 3f, 3f, 0.5f, 1f, baseMat);
            var (_, view, _, remoteViews) = BuildWorld(Vector3.zero, new Vector3(0f, 0f, 20f));
            (_, Func<Camera> cameraOf) = BuildCamera(new Vector3(0f, 10f, 12f));
            BattleVisionBlockerDriver driver = BuildDriver(view, map, cameraOf, baseMat, fadeMat);
            try
            {
                for (int i = 0; i < SettledProbes; i++) driver.Tick(ProbeSeconds);
                Assert.AreSame(baseMat, box.sharedMaterial, "站姿 1m 箱＝露头带——自然部分遮挡，不渐隐");
                Assert.IsTrue(RendererOf(remoteViews[0]).enabled, "低箱不遮站姿头部 ⇒ 角色看得见 → 显示");
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
            (SimMapData map, Renderer wall) = BindBoxObstacle(0f, 3f, 3f, 0.5f, 20f, baseMat);
            var (_, view, _, _) = BuildWorld(Vector3.zero, new Vector3(0f, 0f, 20f));
            (_, Func<Camera> cameraOf) = BuildCamera(new Vector3(0f, 10f, 12f));
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
        public void 角色视觉_墙后全挡隐藏_对角露头显示_本地豁免()
        {
            var (baseMat, fadeMat) = LoadMaterialPair();
            (SimMapData map, Renderer _) = BindBoxObstacle(0f, 3f, 3f, 0.5f, 20f, baseMat);
            // 远端两枚：墙后 (0,0,6)——所有身体采样被墙挡；对角露头 (10,0,6)——视线从墙侧掠过
            var (_, view, localView, remoteViews) = BuildWorld(Vector3.zero,
                new Vector3(0f, 0f, 6f), new Vector3(10f, 0f, 6f));
            (_, Func<Camera> cameraOf) = BuildCamera(new Vector3(0f, 10f, 12f));
            BattleVisionBlockerDriver driver = BuildDriver(view, map, cameraOf, baseMat, fadeMat);
            try
            {
                for (int i = 0; i < SettledProbes; i++) driver.Tick(ProbeSeconds);

                Assert.IsFalse(RendererOf(remoteViews[0]).enabled, "墙后实体（身体采样全挡）隐藏——角色看不见");
                Assert.IsTrue(RendererOf(remoteViews[1]).enabled, "对角露头实体（视线不被挡）照常显示");
                Assert.IsTrue(RendererOf(localView).enabled, "本地玩家豁免——自己永远全渲染");
            }
            finally
            {
                driver.Dispose();
            }
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 角色视觉_远端全挡_相机可见_仍隐藏且不渐隐()
        {
            var (baseMat, fadeMat) = LoadMaterialPair();
            // 墙在远端(0,0,6)与本地(0,0,0)之间；相机在 z=+12（远端一侧），能直接看到远端，
            // 但本地角色眼位被墙全挡（墙高 2.5 > 人体）⇒ 隐藏；全挡敌人不参与渐隐。
            (SimMapData map, Renderer wall) = BindBoxObstacle(0f, 3f, 3f, 0.5f, 2.5f, baseMat);
            var (_, view, _, remoteViews) = BuildWorld(Vector3.zero, new Vector3(0f, 0f, 6f));
            (GameObject camGo, Func<Camera> cameraOf) = BuildCamera(new Vector3(0f, 10f, 12f));
            BattleVisionBlockerDriver driver = BuildDriver(view, map, cameraOf, baseMat, fadeMat);
            try
            {
                Vector3 remoteTop = remoteViews[0].transform.position + Vector3.up * BodyBake.Height;
                Assert.IsFalse(VisionSegment.Blocked(in map.Obstacles[0], camGo.transform.position, remoteTop),
                    "相机在敌人一侧能看到（墙在敌人后方）——相机可见性不是显隐依据");

                for (int i = 0; i < SettledProbes; i++) driver.Tick(ProbeSeconds);
                Assert.IsFalse(RendererOf(remoteViews[0]).enabled, "角色眼位全挡 ⇒ 隐藏（相机看到不算）");
                Assert.AreSame(baseMat, wall.sharedMaterial, "全挡敌人不参与渐隐（防相机透视）");
            }
            finally
            {
                driver.Dispose();
            }
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 角色视觉_远端可见_相机侧掩体挡_远端渐隐()
        {
            var (baseMat, fadeMat) = LoadMaterialPair();
            // 墙 @ (5,9)：在相机(0,10,12)→远端(10,0,6)柱顶线上挡相机，但在本地→远端的角色视线上不挡。
            (SimMapData map, Renderer wall) = BindBoxObstacle(5f, 9f, 1f, 0.5f, 20f, baseMat);
            var (_, view, _, remoteViews) = BuildWorld(Vector3.zero, new Vector3(10f, 0f, 6f));
            (_, Func<Camera> cameraOf) = BuildCamera(new Vector3(0f, 10f, 12f));
            BattleVisionBlockerDriver driver = BuildDriver(view, map, cameraOf, baseMat, fadeMat);
            try
            {
                for (int i = 0; i < SettledProbes; i++) driver.Tick(ProbeSeconds);
                Assert.IsTrue(RendererOf(remoteViews[0]).enabled, "角色视线无遮挡 ⇒ 远端显示");
                Assert.AreSame(fadeMat, wall.sharedMaterial, "相机侧掩体挡住可见远端 ⇒ 远端补位渐隐");
            }
            finally
            {
                driver.Dispose();
            }
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 角色视觉_掩体边缘露身_显示()
        {
            var (baseMat, fadeMat) = LoadMaterialPair();
            // 墙 x∈[-1.1,1.1] @ z≈4：本地(0,0,0) 的柱顶中心线在墙近面 x≈1.05 被挡；
            // 远端(1.8,0,6) 的右侧身体采样在墙近面 x≈1.25 绕过墙缘 ⇒ 角色看得到露身，应显示。
            (SimMapData map, Renderer _) = BindBoxObstacle(0f, 4f, 1.1f, 0.5f, 20f, baseMat);
            var (_, view, _, remoteViews) = BuildWorld(Vector3.zero, new Vector3(1.8f, 0f, 6f));
            (_, Func<Camera> cameraOf) = BuildCamera(new Vector3(0f, 10f, 12f));
            BattleVisionBlockerDriver driver = BuildDriver(view, map, cameraOf, baseMat, fadeMat);
            try
            {
                for (int i = 0; i < SettledProbes; i++) driver.Tick(ProbeSeconds);
                Assert.IsTrue(RendererOf(remoteViews[0]).enabled,
                    "身体边缘绕过掩体 ⇒ 角色看得见 → 显示（不再用单根柱顶中心线误藏）");
            }
            finally
            {
                driver.Dispose();
            }
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 角色视觉_远端视图位滞后_判定取渲染帧快照位()
        {
            // 视图位（插值滞后）会把远端画到墙侧 x=3，看似可见；判定必须取渲染帧快照位
            // （权威位 x=0，墙后全挡）——显隐不随视图插值漂移。
            var (baseMat, fadeMat) = LoadMaterialPair();
            (SimMapData map, Renderer _) = BindBoxObstacle(0f, 4f, 1.5f, 0.5f, 20f, baseMat);   // 墙 x∈[-1.5,1.5] @ z≈4

            var authWorld = new SimWorldState { RngState = 1UL };
            authWorld.Spawn(new EntitySlot { Hp = 100, Pos = ToSim(Vector3.zero) }, out _);
            authWorld.Spawn(new EntitySlot { Hp = 100, Pos = ToSim(new Vector3(0f, 0f, 6f)) }, out _);
            var snapshot = new SimWorldStateSnapshot();
            snapshot.CaptureFull(authWorld);
            snapshot.Frame = 100;

            (SimView view, GameObject remoteView) = BuildStaleRemoteView(0, new Vector3(3f, 0f, 6f), snapshot);
            (_, Func<Camera> cameraOf) = BuildCamera(new Vector3(0f, 10f, 12f));
            BattleVisionBlockerDriver driver = BuildDriver(view, map, cameraOf, baseMat, fadeMat);
            try
            {
                for (int i = 0; i < SettledProbes; i++) driver.Tick(ProbeSeconds);
                Assert.IsFalse(RendererOf(remoteView).enabled,
                    "远端视图位滞后不算数：权威位在墙后全挡 → 隐藏");
            }
            finally
            {
                driver.Dispose();
            }
        }

        /// <summary>
        /// 建"视图位与权威位不一致"的单客户端视图（<paramref name="localSlotIndex"/> 本地、
        /// 另一槽位为远端）：世界按权威快照的槽位序生成（slot0=A、slot1=B），喂同一份快照后
        /// 把**远端视图位**改写成 <paramref name="staleRemoteViewPos"/>（模拟插值滞后）；
        /// 驱动判定取快照位，因此该改写只影响渲染观感、不影响显隐结论。
        /// </summary>
        private (SimView View, GameObject RemoteView) BuildStaleRemoteView(
            int localSlotIndex, Vector3 staleRemoteViewPos, SimWorldStateSnapshot snapshot)
        {
            var world = new SimWorldState { RngState = 1UL };
            long idA = world.Spawn(new EntitySlot { Hp = 100, Pos = ToSim(Vector3.zero) }, out _);
            long idB = world.Spawn(new EntitySlot { Hp = 100, Pos = ToSim(new Vector3(0f, 0f, 6f)) }, out _);
            SimView view = new SimView(world, null,
                factory: (loc, parent) => Scope.CreateGameObject(loc, typeof(MeshRenderer)), recycler: null);
            view.AlignLocal(localSlotIndex == 0 ? idA : idB);
            view.Tick(1f / 60f);                                    // 建视图 + 落位
            view.OnAuthoritativeSnapshot(snapshot);                 // 权威时间线（两端同一份）
            view.TryGetView(1 - localSlotIndex, out GameObject remoteView);
            remoteView.transform.position = staleRemoteViewPos;     // 视图位 ≠ 权威位（渲染用；判定取快照位）
            return (view, remoteView);
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 遮蔽_材质不成对_不渐隐_角色视觉遮蔽照常生效()
        {
            // 墙体渲染件挂非基准材质（内建默认材质——内容配对裁决：不成对即不渐隐）⇒ 零绑定 ⇒ 不渐隐；
            // 但**角色视觉与渐隐解耦**（v3.4）：墙后实体照常被藏——"看不见墙后"不依赖该墙能否渐隐。
            var (baseMat, fadeMat) = LoadMaterialPair();
            GameObject probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Scope.Track(probe);                                     // 原语件（未挂 Scope 树）显式托管
            Material unpairedMat = probe.GetComponent<Renderer>().sharedMaterial;   // 内建默认材质（持久资产，勿 Track 勿销毁）
            (SimMapData map, Renderer wall) = BindBoxObstacle(0f, 3f, 3f, 0.5f, 20f, unpairedMat);
            var (_, view, _, remoteViews) = BuildWorld(Vector3.zero, new Vector3(0f, 0f, 6f));
            (_, Func<Camera> cameraOf) = BuildCamera(new Vector3(0f, 10f, 12f));
            BattleVisionBlockerDriver driver = BuildDriver(view, map, cameraOf, baseMat, fadeMat);
            try
            {
                for (int i = 0; i < SettledProbes; i++) driver.Tick(ProbeSeconds);
                Assert.AreSame(unpairedMat, wall.sharedMaterial, "材质不成对不入绑定——材质永不动");
                Assert.IsFalse(RendererOf(remoteViews[0]).enabled, "角色视觉与渐隐解耦 ⇒ 墙后实体照常隐藏");
            }
            finally
            {
                driver.Dispose();
            }
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 角色视觉_切出遮挡_实体即时恢复_材质同步归位()
        {
            var (baseMat, fadeMat) = LoadMaterialPair();
            (SimMapData map, Renderer wall) = BindBoxObstacle(0f, 3f, 3f, 0.5f, 20f, baseMat);
            var (world, view, _, remoteViews) = BuildWorld(Vector3.zero, new Vector3(0f, 0f, 6f));
            (GameObject camGo, Func<Camera> cameraOf) = BuildCamera(new Vector3(0f, 10f, 12f));
            BattleVisionBlockerDriver driver = BuildDriver(view, map, cameraOf, baseMat, fadeMat);
            try
            {
                for (int i = 0; i < SettledProbes; i++) driver.Tick(ProbeSeconds);
                Assert.IsFalse(RendererOf(remoteViews[0]).enabled, "角色视线全挡 ⇒ 墙后实体隐藏");

                // 远端挪出墙体范围（角色视线不再被墙挡）——隐藏两拍迟滞后恢复
                world.Entities[1].Pos = ToSim(new Vector3(10f, 0f, 6f));
                view.Tick(1f / 60f);                            // 视图位置随 Sim 更新（驱动读的是视图柱顶）
                for (int i = 0; i < HoldProbes; i++) driver.Tick(ProbeSeconds);
                Assert.IsTrue(RendererOf(remoteViews[0]).enabled, "切出影区 ⇒ 渲染开关恢复（不销毁）");

                // 相机挪开（相机→本地柱顶不再穿墙）——渐隐两拍迟滞后材质归位
                camGo.transform.position = new Vector3(20f, 10f, 12f);
                for (int i = 0; i < HoldProbes; i++) driver.Tick(ProbeSeconds);
                Assert.AreSame(baseMat, wall.sharedMaterial, "遮挡清空 ⇒ 材质归位");
            }
            finally
            {
                driver.Dispose();
            }
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 遮蔽_掩体边缘振荡_最短保持不闪隐()
        {
            // 掩体边缘"两拍真两拍假"的振荡（实测日志连续 9 次翻转＝敌人闪隐）：隐藏一旦成立，
            // 最短保持（0.5s＝5 拍）期内不得闪回可见——迟滞只挡单拍抖动，挡不住这种周期。
            var (baseMat, fadeMat) = LoadMaterialPair();
            (SimMapData map, Renderer _) = BindBoxObstacle(0f, 3f, 3f, 0.5f, 20f, baseMat);
            var (world, view, _, remoteViews) = BuildWorld(Vector3.zero, new Vector3(0f, 0f, 6f));
            (_, Func<Camera> cameraOf) = BuildCamera(new Vector3(0f, 10f, 12f));
            BattleVisionBlockerDriver driver = BuildDriver(view, map, cameraOf, baseMat, fadeMat);
            try
            {
                for (int i = 0; i < SettledProbes; i++) driver.Tick(ProbeSeconds);
                Assert.IsFalse(RendererOf(remoteViews[0]).enabled, "先进入隐藏（角色视线全挡）");

                for (int i = 0; i < 4; i++)
                {
                    world.Entities[1].Pos = ToSim(i % 2 == 0
                        ? new Vector3(10f, 0f, 6f)      // 可见（线段从墙侧掠过）
                        : new Vector3(0f, 0f, 6f));     // 被挡
                    view.Tick(1f / 60f);
                    driver.Tick(ProbeSeconds);
                    Assert.IsFalse(RendererOf(remoteViews[0]).enabled,
                        $"振荡第 {i + 1} 拍不得闪回可见（最短保持期内不翻转）");
                }
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
            (SimMapData map, Renderer wall) = BindBoxObstacle(0f, 3f, 3f, 0.5f, 20f, baseMat);
            var (_, view, _, remoteViews) = BuildWorld(Vector3.zero, new Vector3(0f, 0f, 6f));
            (GameObject camGo, _) = BuildCamera(new Vector3(0f, 10f, 12f));
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
            (SimMapData map, Renderer wall) = BindBoxObstacle(0f, 3f, 3f, 0.5f, 20f, baseMat);
            var world = new SimWorldState { RngState = 1UL };
            world.Spawn(new EntitySlot { Hp = 100, Pos = ToSim(Vector3.zero) }, out _);
            SimView view = new SimView(world, null,
                factory: (loc, parent) => Scope.CreateGameObject(loc, typeof(MeshRenderer)),
                recycler: null);                                    // 未 AlignLocal：LocalEntityId=0
            view.Tick(1f / 60f);
            (_, Func<Camera> cameraOf) = BuildCamera(new Vector3(0f, 10f, 12f));
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
            (SimMapData map, Renderer wall) = BindBoxObstacle(0f, 3f, 3f, 0.5f, 20f, baseMat);
            var (_, view, _, remoteViews) = BuildWorld(Vector3.zero, new Vector3(0f, 0f, 6f));
            (_, Func<Camera> cameraOf) = BuildCamera(new Vector3(0f, 10f, 12f));
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
