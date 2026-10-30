using LiteGame.EditorTools;
using LiteSim;
using NUnit.Framework;
using UnityEngine;
using LiteGame.Modules.Map;

namespace LiteGame.Tests.EditMode
{
        /// <summary>
        /// 地图烘焙器（<see cref="SimMapBaker"/>，《地图数据专项设计》§6 规则）的 L2 EditMode 覆盖：
        /// 采集值语义（Box/Sphere/yaw90 交换）、失败面 fail-fast（Mesh/旋转/未知 surface/出生点数量/
        /// **出生点落障碍内、离障碍小于身位半径与越界**（多图批/新图通用闸）/**边界标记缺省未赋值**、
        /// 边界）、确定性（重复采集逐字节一致）、超上限截断＋丢弃报告。
        /// 合成根喂正/负例，不动真实场景。
        /// 实烘对账（Arena01/训练场产物 == 夹具快照 TestMaps.*）由 run_script 取证＋L1 夹具锚把守。
        /// </summary>
        [Category(LiteTesting.TestCategory.Contract)]
        public sealed class SimMapBakerEditModeTests
        {
            private GameObject _root;

            /// <summary>正例里的出生点统一落在 (9,0,9)（远离合成障碍；布点校验后原点会被视作"落在障碍内"）。</summary>
            private static readonly Vector3 SafeSpawn = new Vector3(9f, 0f, 9f);

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
            _root = null;
        }

        // ---- 正例：采集值语义 ----

        [Test]
        public void 采集_最小合法图_值正确且摘要自洽()
        {
            _root = NewRoot();
            Box(_root, "wall", new Vector3(1.25f, 1.25f, 7f), new Vector3(5f, 2.5f, 0.6f));
            Spawn(_root, "spawn_00", new Vector3(-1f, 0f, -1f));

            string report = SimMapBaker.Collect(_root, out SimMapData map, out string json,
                scenePath: BakeTargets.MapScenePath);

            Assert.IsNotNull(map, report);
            StringAssert.StartsWith("已烘焙：", report);
            Assert.AreEqual(1, map.ObstacleCount);
            Assert.AreEqual(1, map.SpawnPointCount);

            SimObstacle o = map.Obstacles[0];
            Assert.AreEqual(SimObstacleKind.Box, o.Kind);
            Assert.AreEqual(1.25f, o.Center.X, 1e-6f);
            Assert.AreEqual(0f, o.Center.Y, 1e-6f, "y 为底高：center.y - halfY");
            Assert.AreEqual(7f, o.Center.Z, 1e-6f);
            Assert.AreEqual(2.5f, o.HalfX, 1e-6f);
            Assert.AreEqual(0.3f, o.HalfZ, 1e-6f);
            Assert.AreEqual(2.5f, o.Height, 1e-6f);
            Assert.AreEqual(SimObstacleSurface.Default, o.Surface);

            Assert.AreEqual(0f, map.GroundY, 1e-6f);
            Assert.AreEqual(100f, map.HalfWidth, 1e-6f);
            Assert.AreEqual(100f, map.HalfDepth, 1e-6f);

            string digest = SimMapDigest.Compute(map);
            StringAssert.Contains("digest=" + digest, report, "报告 digest 须与采集值复算一致");
            StringAssert.Contains("\"content_digest\": \"" + digest + "\"", json);
            StringAssert.Contains("\"map_id\": 1", json);
            StringAssert.Contains("\"name\": \"arena01\"", json);
            StringAssert.Contains("\"scene\": \"" + BakeTargets.MapScenePath + "\"", json);   // 多图批：表行 scene 列（装载/卸载面）
            StringAssert.Contains("\"half_x\": 2.5", json);
        }

        [Test]
        public void 采集_球体_映射为圆且底为球底()
        {
            _root = NewRoot();
            Sphere(_root, "pylon", new Vector3(0f, 3f, 0f), 1f);
            Spawn(_root, "spawn_00", Vector3.zero);

            string report = SimMapBaker.Collect(_root, out SimMapData map, out _);

            Assert.IsNotNull(map, report);
            SimObstacle o = map.Obstacles[0];
            Assert.AreEqual(SimObstacleKind.Circle, o.Kind);
            Assert.AreEqual(1f, o.Radius, 1e-6f);
            Assert.AreEqual(2f, o.Center.Y, 1e-6f);
            Assert.AreEqual(2f, o.Height, 1e-6f);
        }

        [Test]
        public void 采集_yaw90_交换半宽半深()
        {
            _root = NewRoot();
            Box(_root, "rot90", new Vector3(0f, 1.25f, 0f), new Vector3(6f, 2.5f, 0.6f), 90f);
            Spawn(_root, "spawn_00", SafeSpawn);

            string report = SimMapBaker.Collect(_root, out SimMapData map, out _);

            Assert.IsNotNull(map, report);
            SimObstacle o = map.Obstacles[0];
            Assert.AreEqual(0.3f, o.HalfX, 1e-6f, "yaw90：局部深 → 世界宽");
            Assert.AreEqual(3f, o.HalfZ, 1e-6f, "yaw90：局部宽 → 世界深");
        }

        [Test]
        public void 采集_重复运行_逐字节一致()
        {
            _root = NewRoot();
            Box(_root, "b_high", new Vector3(-3f, 1.25f, 4f), new Vector3(5f, 2.5f, 0.6f));
            Box(_root, "a_low", new Vector3(1f, 0.5f, 2f), new Vector3(2f, 1f, 2f));
            Spawn(_root, "spawn_01", new Vector3(9f, 0f, 9f));
            Spawn(_root, "spawn_00", new Vector3(0f, 0f, 0f));

            string r1 = SimMapBaker.Collect(_root, out SimMapData m1, out string j1);
            string r2 = SimMapBaker.Collect(_root, out SimMapData m2, out string j2);

            Assert.IsNotNull(m1, r1);
            Assert.IsNotNull(m2, r2);
            Assert.AreEqual(j1, j2, "同输入重复采集须逐字节一致");
            Assert.AreEqual(0f, m1.SpawnPoints[0].X, 1e-6f, "出生点按名 Ordinal 排序：spawn_00 在前");
            Assert.AreEqual(1f, m1.Obstacles[0].HalfX, 1e-6f, "障碍按名 Ordinal 排序：a_low 在前");
        }

        // ---- 负例：fail-fast（ABORT 报告、不产半成品） ----

        [Test]
        public void 校验_网格碰撞体_默认拒载()
        {
            _root = NewRoot();
            PrimitiveMesh(_root, "mesh_proxy");
            Spawn(_root, "spawn_00", Vector3.zero);

            string report = SimMapBaker.Collect(_root, out SimMapData map, out string json);

            Assert.IsNull(map, "MeshCollider 默认策略必须拒载");
            Assert.IsNull(json, "拒载时不得产出 JSON");
            StringAssert.StartsWith("ABORT", report);
            StringAssert.Contains("mesh_proxy", report);
            StringAssert.Contains("MeshCollider 拒载", report);
        }

        [Test]
        public void 校验_网格碰撞体_ReportOnly_提取单盒AABB()
        {
            _root = NewRoot();
            PrimitiveMesh(_root, "mesh_proxy");
            Spawn(_root, "spawn_00", SafeSpawn);

            string report = SimMapBaker.Collect(_root, out SimMapData map, out _, MeshColliderPolicy.ReportOnly);

            Assert.IsNotNull(map, report);
            SimObstacle o = map.Obstacles[0];
            Assert.AreEqual(SimObstacleKind.Box, o.Kind);
            Assert.AreEqual(0.5f, o.HalfX, 1e-6f);
            Assert.AreEqual(0.5f, o.HalfZ, 1e-6f);
            Assert.AreEqual(1f, o.Height, 1e-6f);
            StringAssert.Contains("单盒 AABB", report);
        }

        [Test]
        public void 校验_yaw非90倍数_拒载()
        {
            _root = NewRoot();
            Box(_root, "tilted", new Vector3(0f, 0.5f, 0f), Vector3.one, 45f);
            Spawn(_root, "spawn_00", Vector3.zero);

            string report = SimMapBaker.Collect(_root, out SimMapData map, out _);

            Assert.IsNull(map);
            StringAssert.Contains("tilted", report);
            StringAssert.Contains("非 90° 倍数", report);
        }

        [Test]
        public void 校验_pitch_拒载()
        {
            _root = NewRoot();
            GameObject go = Box(_root, "pitched", new Vector3(0f, 0.5f, 0f), Vector3.one);
            go.transform.localRotation = Quaternion.Euler(30f, 0f, 0f);
            Spawn(_root, "spawn_00", Vector3.zero);

            string report = SimMapBaker.Collect(_root, out SimMapData map, out _);

            Assert.IsNull(map);
            StringAssert.Contains("pitch/roll", report);
        }

        [Test]
        public void 校验_表面前缀_已知映射()
        {
            _root = NewRoot();
            Box(_root, "surf_concrete_box_01", new Vector3(0f, 0.5f, 0f), Vector3.one);
            Spawn(_root, "spawn_00", SafeSpawn);

            string report = SimMapBaker.Collect(_root, out SimMapData map, out _);

            Assert.IsNotNull(map, report);
            Assert.AreEqual(SimObstacleSurface.Concrete, map.Obstacles[0].Surface);
        }

        [Test]
        public void 校验_表面前缀_未知名拒载()
        {
            _root = NewRoot();
            Box(_root, "surf_bogus_box", new Vector3(0f, 0.5f, 0f), Vector3.one);
            Spawn(_root, "spawn_00", Vector3.zero);

            string report = SimMapBaker.Collect(_root, out SimMapData map, out _);

            Assert.IsNull(map);
            StringAssert.Contains("未知名", report);
            StringAssert.Contains("bogus", report);
        }

        [Test]
        public void 校验_出生点落障碍内与越界_拒载()
        {
            // 多图批：布点可调的安全闸——出生点卡在障碍里（含 y 区间；此处 1m 低箱，站姿 1.8m 必然相交）
            _root = NewRoot();
            Box(_root, "cover", new Vector3(0f, 0.5f, 0f), new Vector3(2f, 1f, 2f));
            Spawn(_root, "spawn_00", Vector3.zero);

            string inside = SimMapBaker.Collect(_root, out SimMapData m0, out _);
            Assert.IsNull(m0, "出生点落在障碍内必须拒载（不产半成品）");
            StringAssert.Contains("spawn_00", inside);
            StringAssert.Contains("出生点落在障碍内", inside);

            // 越界：布点必须在 map_bounds 内（视觉场地与判定同界）
            Object.DestroyImmediate(_root);
            _root = NewRoot();
            Spawn(_root, "spawn_00", new Vector3(0f, 0f, 0f));
            Spawn(_root, "spawn_01", new Vector3(101f, 0f, 0f));     // bounds ±100（NewRoot 的 map_bounds）
            Spawn(_root, "spawn_02", new Vector3(0f, 0f, 101f));

            string outside = SimMapBaker.Collect(_root, out SimMapData m1, out _);
            Assert.IsNull(m1, "出生点越界必须拒载");
            StringAssert.Contains("spawn_01", outside);
            StringAssert.Contains("出生点越界", outside);

            // 边界外但同名的合法点不受影响（1m 低箱顶面之上的悬空点不判"落在障碍内"——y 区间不相交）
            Object.DestroyImmediate(_root);
            _root = NewRoot();
            Box(_root, "cover", new Vector3(0f, 0.5f, 0f), new Vector3(2f, 1f, 2f));
            Spawn(_root, "spawn_00", new Vector3(0f, 5f, 0f));

            string floating = SimMapBaker.Collect(_root, out SimMapData m2, out _);
            Assert.IsNotNull(m2, floating);
        }

        [Test]
        public void 校验_出生点离障碍小于身位半径_拒载()
        {
            // 新图通用闸：不"落在障碍内"还不够——身位圆柱半径内有障碍 ⇒ 出生首帧被移动去穿插推出，
            // 落点与场景标记不符（旧实现只在"点在截面内"时拦，靠作者记得留余量）。
            _root = NewRoot();
            Box(_root, "cover", new Vector3(0f, 0.5f, 0f), new Vector3(2f, 1f, 2f));   // 盒面 x=1
            float gap = BodyBake.Radius * 0.5f;
            Spawn(_root, "spawn_00", new Vector3(1f + gap, 0f, 0f));

            string report = SimMapBaker.Collect(_root, out SimMapData map, out _);

            Assert.IsNull(map, "出生点离障碍小于身位半径必须拒载（出生首帧会被去穿插推出）");
            StringAssert.Contains("spawn_00", report);
            StringAssert.Contains("出生点离障碍过近", report);

            // 同点退到身位半径之外即合法（闸只卡"会被推"的净距，不额外要求宽敞布点）
            Object.DestroyImmediate(_root);
            _root = NewRoot();
            Box(_root, "cover", new Vector3(0f, 0.5f, 0f), new Vector3(2f, 1f, 2f));
            Spawn(_root, "spawn_00", new Vector3(1f + BodyBake.Radius + 0.01f, 0f, 0f));

            string clear = SimMapBaker.Collect(_root, out SimMapData ok, out _);
            Assert.IsNotNull(ok, clear);
        }

        [Test]
        public void 校验_边界标记缺省未赋值_拒载()
        {
            // 新图通用闸：map_bounds 半宽/半深**必填**（默认 0 不再静默成 ±100——那种默认会让边界钳制
            // 变成死代码、content_digest 与真实场地脱钩）。
            _root = new GameObject(BakeTargets.MapCollisionRoot);
            var bounds = new GameObject(BakeTargets.MapBoundsObject);
            bounds.transform.SetParent(_root.transform, false);
            bounds.AddComponent<MapBoundsMarker>();              // 作者漏赋值：缺省 0
            Spawn(_root, "spawn_00", Vector3.zero);

            string report = SimMapBaker.Collect(_root, out SimMapData map, out _);

            Assert.IsNull(map, "边界未显式赋值（半宽/半深 0）必须拒载");
            StringAssert.Contains("边界半宽/半深须为正", report);
        }

        [Test]
        public void 校验_出生点零个与超限_拒载()
        {
            _root = NewRoot();
            Box(_root, "wall", new Vector3(0f, 0.5f, 0f), Vector3.one);

            string none = SimMapBaker.Collect(_root, out SimMapData m0, out _);
            Assert.IsNull(m0);
            StringAssert.Contains("出生点为 0", none);

            for (int i = 0; i < 17; i++) Spawn(_root, $"spawn_{i:00}", new Vector3(i, 0f, 0f));
            string over = SimMapBaker.Collect(_root, out SimMapData m1, out _);
            Assert.IsNull(m1);
            StringAssert.Contains("> 上限", over);
        }

        [Test]
        public void 校验_出生标记挂碰撞体_拒载()
        {
            _root = NewRoot();
            Box(_root, "wall", new Vector3(0f, 0.5f, 0f), Vector3.one);
            GameObject spawn = Spawn(_root, "spawn_00", Vector3.zero);
            spawn.AddComponent<BoxCollider>();

            string report = SimMapBaker.Collect(_root, out SimMapData map, out _);

            Assert.IsNull(map);
            StringAssert.Contains("出生标记件不得挂碰撞体", report);
        }

        [Test]
        public void 校验_缺边界标注_拒载()
        {
            _root = new GameObject(BakeTargets.MapCollisionRoot);
            Box(_root, "wall", new Vector3(0f, 0.5f, 0f), Vector3.one);
            Spawn(_root, "spawn_00", Vector3.zero);

            string report = SimMapBaker.Collect(_root, out SimMapData map, out _);

            Assert.IsNull(map, "缺 map_bounds 必须拒载（禁硬编码回退）");
            StringAssert.Contains(BakeTargets.MapBoundsObject, report);
        }

        // ---- 超上限：面积降序截断＋丢弃清单（禁止静默截断） ----

        [Test]
        public void 截断_超上限_面积降序保留并报告丢弃()
        {
            _root = NewRoot();
            for (int i = 0; i < SimMapData.MaxObstacles; i++)
                Box(_root, $"cap_{i:000}", new Vector3(i * 0.5f, 0.5f, 0f), new Vector3(2f, 1f, 2f));
            Box(_root, "tiny_dropped", new Vector3(0f, 0.5f, 50f), new Vector3(0.5f, 1f, 0.5f));
            Spawn(_root, "spawn_00", new Vector3(0f, 0f, 60f));

            string report = SimMapBaker.Collect(_root, out SimMapData map, out _);

            Assert.IsNotNull(map, report);
            Assert.AreEqual(SimMapData.MaxObstacles, map.ObstacleCount);
            StringAssert.Contains("超上限", report);
            StringAssert.Contains("tiny_dropped", report, "丢弃清单必须点名（禁止静默截断）");
        }

        // ---- 合成根原语 ----

        private static GameObject NewRoot()
        {
            var root = new GameObject(BakeTargets.MapCollisionRoot);
            var bounds = new GameObject(BakeTargets.MapBoundsObject);
            bounds.transform.SetParent(root.transform, false);
            var marker = bounds.AddComponent<MapBoundsMarker>();
            marker.HalfWidth = 100f;                    // 标记件半宽/半深必填（缺省 0 = 烘焙拒载）——夹具显式赋合法值
            marker.HalfDepth = 100f;
            return root;
        }

        private static GameObject Box(GameObject root, string name, Vector3 pos, Vector3 scale, float yawDeg = 0f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            if (yawDeg != 0f) go.transform.localRotation = Quaternion.Euler(0f, yawDeg, 0f);
            go.AddComponent<BoxCollider>();
            return go;
        }

        private static GameObject Sphere(GameObject root, string name, Vector3 pos, float radius)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = pos;
            var collider = go.AddComponent<SphereCollider>();
            collider.radius = radius;
            return go;
        }

        private static GameObject Spawn(GameObject root, string name, Vector3 pos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = pos;
            return go;
        }

        /// <summary>真网格碰撞体（primitive Cube 的网格；铲掉 BoxCollider 只留 MeshCollider）。</summary>
        private static GameObject PrimitiveMesh(GameObject root, string name)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(root.transform, false);
            Object.DestroyImmediate(go.GetComponent<BoxCollider>());
            var mesh = go.AddComponent<MeshCollider>();
            mesh.sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
            return go;
        }
    }
}
