using System;
using System.Collections.Generic;
using System.Text;
using LiteSim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LiteGame.EditorTools
{
    /// <summary>MeshCollider 策略（D5）：默认拒载；ReportOnly＝提取单盒 AABB 并入图＋报告过近似。</summary>
    public enum MeshColliderPolicy
    {
        Fail = 0,
        ReportOnly = 1,
    }

    /// <summary>
    /// 地图烘焙器（烘焙工具箱「地图」页，或 run_script 直调；《地图数据专项设计》§6 规则全量）：
    /// 采集地图场景（<see cref="BakeTargets.MapScenePath"/>）的判定代理（`MapCollision` 树）＋出生点
    /// （`spawn_*`）＋边界（`map_bounds` 的 <see cref="MapBoundsMarker"/>，禁硬编码）→ 校验（fail-fast，
    /// 不产半成品）→ 毫米量化（round half away from zero）→ 固定排序 → <see cref="SimMapDigest"/> →
    /// 写 `Luban/Data/map.json`（write-if-changed）。
    ///
    /// **产物是 Luban 数据源**（非 .bytes）：不自动跑 gen.bat（D6）——`.bytes` 由 `Luban/gen.bat` 从本
    /// JSON 产出；改场景 = 重烘本 JSON，**勿手改**（"源＝烘焙产物"闭环）。
    /// **确定性**：同场景重复烘焙逐字节一致（固定 key 排序 ＋ 毫米量化 ＋ InvariantCulture ＋ LF）。
    /// **失败口径**：预期失败返回 <c>ABORT</c> 多行报告（含对象路径定位），不抛异常（工具箱统一形态）。
    /// 采集规则：Box→Box、Sphere→Circle（须均匀缩放）、Capsule 按方向（Y→Circle／X,Z→Box）、
    /// MeshCollider 按 <see cref="MeshColliderPolicy"/>；旋转仅 yaw∈{0,90,180,270}（pitch/roll 拒载）；
    /// surface 走代理名前缀 `surf_&lt;surface&gt;_`（无前缀＝Default，未知名拒载）；超 256 件按占位面积
    /// 降序取前 256 并报告丢弃清单（禁止静默截断）。
    /// </summary>
    public static class SimMapBaker
    {
        private const int CapsuleAxisX = 0;
        private const int CapsuleAxisY = 1;
        private const int CapsuleAxisZ = 2;
        private const float YawEpsDeg = 0.01f;      // yaw 对 90° 倍数的允许偏差
        private const float UpAxisEps = 0.001f;     // up 轴与 +Y 的偏差容差（pitch/roll 判据）
        private const float ScaleEps = 0.0001f;     // 均匀缩放容差（球/胶囊）

        /// <summary>工具箱/run_script 入口：定位场景 MapCollision 根 → <see cref="Collect"/> → 写盘 → 报告。</summary>
        public static string All()
        {
            if (!System.IO.File.Exists(BakeTargets.MapScenePath))
                return "ABORT 缺地图场景：" + BakeTargets.MapScenePath;

            Scene scene = SceneManager.GetSceneByPath(BakeTargets.MapScenePath);
            bool openedHere = false;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                try
                {
                    scene = EditorSceneManager.OpenScene(BakeTargets.MapScenePath, OpenSceneMode.Additive);
                    openedHere = true;
                }
                catch (Exception ex)
                {
                    return "ABORT 打开地图场景失败：" + ex.Message;
                }
            }

            try
            {
                GameObject root = null;
                foreach (GameObject go in scene.GetRootGameObjects())
                {
                    if (go.name == BakeTargets.MapCollisionRoot) { root = go; break; }
                }
                if (root == null)
                    return $"ABORT 场景「{BakeTargets.MapScenePath}」缺 {BakeTargets.MapCollisionRoot} 根"
                        + "（地图场景约定：MapCollision 代理树 ＋ spawn_* ＋ map_bounds）";

                string report = Collect(root, out SimMapData map, out string jsonText);
                if (map == null) return report;

                var sb = new StringBuilder(report);
                if (scene.isDirty)
                    sb.AppendLine("注意：场景有未保存改动——本次烘焙包含未保存状态（建议先保存场景再重烘）");
                sb.AppendLine(BakeTargets.WriteArtifact(BakeTargets.MapOutput, jsonText));
                sb.Append("提醒：产物是 Luban 数据源（非 .bytes）——需手动跑 Luban/gen.bat；"
                    + "跑完重跑 python scripts/codegen/gen-build-hash.py（生成读码面在 BuildHash 闭包内）。");
                return sb.ToString();
            }
            finally
            {
                if (openedHere) EditorSceneManager.CloseScene(scene, true);
            }
        }

        /// <summary>
        /// 采集＋校验＋量化＋摘要（**不落盘**——写盘归 <see cref="All"/>；测试与 run_script 对账直调）。
        /// 失败返回 ABORT 报告并置 <paramref name="map"/>/<paramref name="jsonText"/> 为 null；
        /// 成功返回采集报告（含丢弃清单等提示行），产物经 out 交给调用方。
        /// </summary>
        public static string Collect(GameObject collisionRoot, out SimMapData map, out string jsonText,
            MeshColliderPolicy meshPolicy = MeshColliderPolicy.Fail)
        {
            map = null;
            jsonText = null;
            if (collisionRoot == null) return "ABORT 采集根为空";

            var errors = new StringBuilder();
            var notes = new StringBuilder();
            var drafts = new List<ObstacleDraft>();

            CollectObstacles(collisionRoot, meshPolicy, drafts, errors, notes);
            CollectBounds(collisionRoot, out float groundY, out float halfWidth, out float halfDepth, errors);
            List<(string Name, SimVector3 Pos)> spawns = CollectSpawns(collisionRoot, errors);

            if (errors.Length > 0)
                return "ABORT 地图采集校验失败（不产半成品——修场景后重烘）：\n" + errors;

            // 超上限：面积降序取前 MaxObstacles（丢弃清单进报告——禁止静默截断）；
            // 产物条目按名 Ordinal 定序（固定 key——确定性输入）
            if (drafts.Count > SimMapData.MaxObstacles)
            {
                drafts.Sort(CompareByAreaDesc);
                List<ObstacleDraft> dropped = drafts.GetRange(SimMapData.MaxObstacles, drafts.Count - SimMapData.MaxObstacles);
                notes.Append("超上限：采集 ").Append(drafts.Count).Append(" 件 > ").Append(SimMapData.MaxObstacles)
                     .Append("——按占位面积降序保留前 ").Append(SimMapData.MaxObstacles)
                     .Append("，丢弃 ").Append(dropped.Count).Append(" 件：");
                for (int i = 0; i < dropped.Count; i++) notes.Append(i == 0 ? " " : ", ").Append(dropped[i].Name);
                notes.Append('\n');
                drafts.RemoveRange(SimMapData.MaxObstacles, drafts.Count - SimMapData.MaxObstacles);
            }
            drafts.Sort(CompareByName);

            var obstacleArray = new SimObstacle[drafts.Count];
            for (int i = 0; i < drafts.Count; i++) obstacleArray[i] = drafts[i].Obstacle;
            var spawnArray = new SimVector3[spawns.Count];
            for (int i = 0; i < spawns.Count; i++) spawnArray[i] = spawns[i].Pos;

            try
            {
                map = SimMapBuilder.Build(groundY, halfWidth, halfDepth,
                    obstacleArray, obstacleArray.Length, spawnArray, spawnArray.Length);
            }
            catch (Exception ex)
            {
                map = null;
                return "ABORT 地图构建失败（越界/非法值）：" + ex.Message;
            }

            string digest = SimMapDigest.Compute(map);
            jsonText = BuildJson(BakeTargets.MapId, BakeTargets.MapName, digest, map);

            var sb = new StringBuilder();
            sb.Append("已烘焙：").Append(map.ObstacleCount).Append(" 障碍 ＋ ").Append(map.SpawnPointCount)
              .Append(" 出生点（边界 ground_y=").Append(SimMapDigest.Fmt(map.GroundY))
              .Append(" half_width=").Append(SimMapDigest.Fmt(map.HalfWidth))
              .Append(" half_depth=").Append(SimMapDigest.Fmt(map.HalfDepth)).Append("）\n");
            sb.Append("digest=").Append(digest).Append('\n');
            sb.Append(notes);
            return sb.ToString();
        }

        // ---- 采集面 ----

        /// <summary>障碍代理采集：Box/Sphere/Capsule 按形定形，MeshCollider 按策略；坏件全部入错误清单。</summary>
        private static void CollectObstacles(GameObject root, MeshColliderPolicy meshPolicy,
            List<ObstacleDraft> drafts, StringBuilder errors, StringBuilder notes)
        {
            foreach (Collider collider in root.GetComponentsInChildren<Collider>())
            {
                GameObject go = collider.gameObject;
                string path = PathOf(collider.transform, root.transform);

                if (go.name == BakeTargets.MapBoundsObject)
                {
                    errors.Append(path).Append("：边界标注件不得挂碰撞体\n");
                    continue;
                }
                if (go.name.StartsWith(BakeTargets.SpawnPrefix, StringComparison.Ordinal))
                {
                    errors.Append(path).Append("：出生标记件不得挂碰撞体\n");
                    continue;
                }

                SimObstacle obstacle;
                string error;
                if (collider is BoxCollider box) error = TryBox(box, out obstacle);
                else if (collider is SphereCollider sphere) error = TrySphere(sphere, out obstacle);
                else if (collider is CapsuleCollider capsule) error = TryCapsule(capsule, out obstacle);
                else if (collider is MeshCollider mesh)
                {
                    if (meshPolicy == MeshColliderPolicy.Fail)
                    {
                        errors.Append(path).Append("：MeshCollider 拒载（默认策略；可切 ReportOnly＝提取单盒 AABB）\n");
                        continue;
                    }
                    error = TryMeshAabb(mesh, out obstacle, out string extractNote);
                    if (error == null)
                        notes.Append(path).Append("：MeshCollider→单盒 AABB 过近似（").Append(extractNote).Append("）\n");
                }
                else
                {
                    errors.Append(path).Append("：不支持碰撞体类型 ").Append(collider.GetType().Name).Append('\n');
                    continue;
                }

                if (error != null)
                {
                    errors.Append(path).Append("：").Append(error).Append('\n');
                    continue;
                }

                error = TrySurface(go.name, out SimObstacleSurface surface);
                if (error != null)
                {
                    errors.Append(path).Append("：").Append(error).Append('\n');
                    continue;
                }
                obstacle.Surface = surface;

                float footprint = obstacle.Kind == SimObstacleKind.Circle   // lint-allow R3（整型等值，非浮点精度比较）
                    ? Mathf.PI * obstacle.Radius * obstacle.Radius
                    : 4f * obstacle.HalfX * obstacle.HalfZ;
                drafts.Add(new ObstacleDraft(go.name, obstacle, footprint));
            }
        }

        private static string TryBox(BoxCollider box, out SimObstacle obstacle)
        {
            obstacle = default;
            Transform t = box.transform;
            string error = CheckYawOnly(t);
            if (error != null) return error;
            error = RequirePositiveScale(t.lossyScale);
            if (error != null) return error;
            if (!IsFinite(box.center) || !IsFinite(box.size)) return "碰撞体中心/尺寸非有限值";

            Vector3 s = t.lossyScale;
            Vector3 localHalf = Vector3.Scale(box.size, new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z))) * 0.5f;
            // 世界轴投影（yaw 仅 90° 倍数：|right.x|/|forward.x| ∈ {0,1}，90/270 自然交换 X/Z）
            float halfX = Mathf.Abs(t.right.x) * localHalf.x + Mathf.Abs(t.forward.x) * localHalf.z;
            float halfZ = Mathf.Abs(t.right.z) * localHalf.x + Mathf.Abs(t.forward.z) * localHalf.z;
            return MakeBox(t.TransformPoint(box.center), halfX, localHalf.y, halfZ, out obstacle);
        }

        private static string TrySphere(SphereCollider sphere, out SimObstacle obstacle)
        {
            obstacle = default;
            Transform t = sphere.transform;
            string error = CheckYawOnly(t);
            if (error != null) return error;
            Vector3 s = t.lossyScale;
            error = RequirePositiveScale(s);
            if (error != null) return error;
            if (!IsFinite(sphere.center)) return "碰撞体中心非有限值";
            if (Mathf.Abs(s.x - s.y) > ScaleEps || Mathf.Abs(s.x - s.z) > ScaleEps)
                return "非均匀缩放（球形代理仅支持均匀缩放——XZ 圆截面不能表椭圆）";

            float radius = sphere.radius * s.x;
            float r = Q(radius);
            if (!(r > 0f)) return "半径量化后非正";
            Vector3 center = t.TransformPoint(sphere.center);
            obstacle = new SimObstacle
            {
                Kind = SimObstacleKind.Circle,
                Center = new SimVector3(Q(center.x), Q(center.y - radius), Q(center.z)),
                Radius = r,
                Height = Q(radius * 2f),
            };
            return null;
        }

        private static string TryCapsule(CapsuleCollider capsule, out SimObstacle obstacle)
        {
            obstacle = default;
            Transform t = capsule.transform;
            Vector3 s = t.lossyScale;
            string error = RequirePositiveScale(s);
            if (error != null) return error;
            if (!IsFinite(capsule.center)) return "碰撞体中心非有限值";
            if (Mathf.Abs(s.x - s.y) > ScaleEps || Mathf.Abs(s.x - s.z) > ScaleEps)
                return "非均匀缩放（胶囊代理仅支持均匀缩放——截面圆不能表椭圆）";
            error = CheckYawOnly(t);
            if (error != null) return error;

            float radius = capsule.radius * s.x;
            float height = Mathf.Max(capsule.height, capsule.radius * 2f) * s.y;   // Unity 总高含端帽
            Vector3 center = t.TransformPoint(capsule.center);

            if (capsule.direction == CapsuleAxisY)
            {
                float r = Q(radius), h = Q(height);
                if (!(r > 0f) || !(h > 0f)) return "半径/高度量化后非正";
                obstacle = new SimObstacle
                {
                    Kind = SimObstacleKind.Circle,
                    Center = new SimVector3(Q(center.x), Q(center.y - height * 0.5f), Q(center.z)),
                    Radius = r,
                    Height = h,
                };
                return null;
            }

            // X/Z 向：单盒 AABB 过近似（沿轴 = 半高，侧向 = 半径）
            float along = height * 0.5f;
            float halfXLocal = capsule.direction == CapsuleAxisX ? along : radius;
            float halfZLocal = capsule.direction == CapsuleAxisX ? radius : along;
            float halfX = Mathf.Abs(t.right.x) * halfXLocal + Mathf.Abs(t.forward.x) * halfZLocal;
            float halfZ = Mathf.Abs(t.right.z) * halfXLocal + Mathf.Abs(t.forward.z) * halfZLocal;
            return MakeBox(center, halfX, radius, halfZ, out obstacle);
        }

        /// <summary>MeshCollider（ReportOnly 策略）：网格局部包围盒 8 角变换取世界 AABB，单盒过近似。</summary>
        private static string TryMeshAabb(MeshCollider meshCollider, out SimObstacle obstacle, out string extractNote)
        {
            obstacle = default;
            extractNote = null;
            Mesh mesh = meshCollider.sharedMesh;
            if (mesh == null) return "MeshCollider 缺网格（sharedMesh 为空）";
            Bounds lb = mesh.bounds;
            if (!IsFinite(lb.center) || !IsFinite(lb.size)) return "网格包围盒非有限值";

            Transform t = meshCollider.transform;
            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            for (int i = 0; i < 8; i++)
            {
                var signs = new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f);
                Vector3 corner = t.TransformPoint(lb.center + Vector3.Scale(lb.extents, signs));
                min = Vector3.Min(min, corner);
                max = Vector3.Max(max, corner);
            }

            string error = MakeBox((min + max) * 0.5f, (max.x - min.x) * 0.5f, (max.y - min.y) * 0.5f, (max.z - min.z) * 0.5f, out obstacle);
            if (error != null) return error;
            extractNote = "half_x=" + SimMapDigest.Fmt(obstacle.HalfX)
                + " half_z=" + SimMapDigest.Fmt(obstacle.HalfZ)
                + " height=" + SimMapDigest.Fmt(obstacle.Height);
            return null;
        }

        private static string MakeBox(Vector3 worldCenter, float halfX, float halfY, float halfZ, out SimObstacle obstacle)
        {
            obstacle = default;
            float hx = Q(halfX), hy = Q(halfY), hz = Q(halfZ);
            if (!(hx > 0f) || !(hy > 0f) || !(hz > 0f)) return "尺寸量化后非正";
            obstacle = new SimObstacle
            {
                Kind = SimObstacleKind.Box,
                Center = new SimVector3(Q(worldCenter.x), Q(worldCenter.y - halfY), Q(worldCenter.z)),
                HalfX = hx,
                HalfZ = hz,
                Height = Q(halfY * 2f),
            };
            return null;
        }

        /// <summary>边界（<see cref="MapBoundsMarker"/>，禁硬编码）：对象名须为 map_bounds 且挂标记组件、唯一。</summary>
        private static void CollectBounds(GameObject root, out float groundY, out float halfWidth, out float halfDepth, StringBuilder errors)
        {
            groundY = halfWidth = halfDepth = 0f;
            Transform bounds = BakeTargets.FindDeep(root.transform, BakeTargets.MapBoundsObject);
            if (bounds == null)
            {
                errors.Append("缺 ").Append(BakeTargets.MapBoundsObject).Append(" 标注件（边界/地面高度唯一来源——禁硬编码）\n");
                return;
            }
            var marker = bounds.GetComponent<MapBoundsMarker>();
            if (marker == null)
            {
                errors.Append(BakeTargets.MapBoundsObject).Append(" 缺 MapBoundsMarker 组件\n");
                return;
            }
            int markerCount = root.GetComponentsInChildren<MapBoundsMarker>().Length;
            if (markerCount > 1)
            {
                errors.Append("MapBoundsMarker 多于一件（").Append(markerCount).Append("）——边界来源须唯一\n");
                return;
            }
            if (!IsFinite(marker.GroundY) || !IsFinite(marker.HalfWidth) || !IsFinite(marker.HalfDepth))
            {
                errors.Append("MapBoundsMarker 含非有限值\n");
                return;
            }
            groundY = Q(marker.GroundY);
            halfWidth = Q(marker.HalfWidth);
            halfDepth = Q(marker.HalfDepth);
            if (!(halfWidth > 0f) || !(halfDepth > 0f))
                errors.Append("边界半宽/半深须为正（").Append(BakeTargets.MapBoundsObject).Append("）\n");
        }

        /// <summary>出生点采集：名前缀 spawn_，按名 Ordinal 排序定 index；0 个/>16 个拒载。</summary>
        private static List<(string Name, SimVector3 Pos)> CollectSpawns(GameObject root, StringBuilder errors)
        {
            var spawns = new List<(string Name, SimVector3 Pos)>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>())
            {
                if (t == root.transform) continue;
                if (!t.name.StartsWith(BakeTargets.SpawnPrefix, StringComparison.Ordinal)) continue;
                Vector3 p = t.position;
                if (!IsFinite(p))
                {
                    errors.Append(PathOf(t, root.transform)).Append("：位置非有限值\n");
                    continue;
                }
                spawns.Add((t.name, new SimVector3(Q(p.x), Q(p.y), Q(p.z))));
            }
            spawns.Sort(CompareSpawns);
            if (spawns.Count == 0)
                errors.Append("出生点为 0（至少 1 个 ").Append(BakeTargets.SpawnPrefix).Append("NN 标记）\n");
            else if (spawns.Count > SimMapData.MaxSpawnPoints)
                errors.Append("出生点 ").Append(spawns.Count).Append(" 个 > 上限 ").Append(SimMapData.MaxSpawnPoints).Append('\n');
            return spawns;
        }

        /// <summary>surface 前缀解析（`surf_&lt;surface&gt;_...`；无前缀＝Default；未知名拒载）。</summary>
        private static string TrySurface(string objectName, out SimObstacleSurface surface)
        {
            surface = SimObstacleSurface.Default;
            if (!objectName.StartsWith(BakeTargets.SurfacePrefix, StringComparison.Ordinal)) return null;
            string rest = objectName.Substring(BakeTargets.SurfacePrefix.Length);
            int cut = rest.IndexOf('_');
            string token = cut < 0 ? rest : rest.Substring(0, cut);
            if (token.Length == 0)
                return "surface 前缀缺类型名（约定 " + BakeTargets.SurfacePrefix + "<surface>_...，如 surf_concrete_box_01）";

            var names = new StringBuilder();
            foreach (SimObstacleSurface candidate in Enum.GetValues(typeof(SimObstacleSurface)))
            {
                if (string.Equals(token, candidate.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    surface = candidate;
                    return null;
                }
                if (names.Length > 0) names.Append('/');
                names.Append(candidate);
            }
            return "surface 前缀未知名「" + token + "」（合法：" + names + "）";
        }

        // ---- 校验与变换原语 ----

        /// <summary>旋转仅接受 yaw∈{0,90,180,270}（pitch/roll 或非 90° 倍数拒载）。</summary>
        private static string CheckYawOnly(Transform t)
        {
            Vector3 up = t.up;
            if (Mathf.Abs(up.x) > UpAxisEps || Mathf.Abs(up.z) > UpAxisEps || up.y < 0.999f)
                return "含 pitch/roll 旋转（仅支持 yaw 仅 90° 倍数——摆正后重烘）";
            float yaw = Mathf.Repeat(Mathf.Atan2(-t.right.z, t.right.x) * Mathf.Rad2Deg, 360f);
            if (Mathf.Abs(Mathf.DeltaAngle(yaw, Mathf.Round(yaw / 90f) * 90f)) > YawEpsDeg)
                return "yaw=" + SimMapDigest.Fmt(yaw) + "° 非 90° 倍数（仅支持 0/90/180/270）";
            return null;
        }

        private static string RequirePositiveScale(Vector3 s)
            => (!(s.x > 0f) || !(s.y > 0f) || !(s.z > 0f)) ? "缩放非正/非有限（损失缩放须为正）" : null;

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        private static bool IsFinite(Vector3 v) => IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z);

        /// <summary>毫米量化（round half away from zero——与消费侧 Sim 定稿同规；《地图数据专项设计》§6）。</summary>
        private static float Q(float v) => (float)(Math.Round((double)v * 1000.0, MidpointRounding.AwayFromZero) / 1000.0);

        private static string PathOf(Transform t, Transform root)
        {
            var sb = new StringBuilder(t.name);
            for (Transform p = t.parent; p != null && p != root.parent; p = p.parent)
                sb.Insert(0, p.name + "/");
            return sb.ToString();
        }

        // ---- 排序（固定 key：名 Ordinal 主序 ＋ 位置/形状次序——确定性单源）----

        private static int CompareByAreaDesc(ObstacleDraft a, ObstacleDraft b)
        {
            int c = b.Footprint.CompareTo(a.Footprint);
            return c != 0 ? c : string.CompareOrdinal(a.Name, b.Name);
        }

        private static int CompareByName(ObstacleDraft a, ObstacleDraft b)
        {
            int c = string.CompareOrdinal(a.Name, b.Name);
            if (c != 0) return c;
            c = a.Obstacle.Center.X.CompareTo(b.Obstacle.Center.X);
            if (c != 0) return c;
            c = a.Obstacle.Center.Y.CompareTo(b.Obstacle.Center.Y);
            if (c != 0) return c;
            c = a.Obstacle.Center.Z.CompareTo(b.Obstacle.Center.Z);
            if (c != 0) return c;
            c = ((byte)a.Obstacle.Kind).CompareTo((byte)b.Obstacle.Kind);
            if (c != 0) return c;
            c = a.Obstacle.Radius.CompareTo(b.Obstacle.Radius);
            if (c != 0) return c;
            c = a.Obstacle.HalfX.CompareTo(b.Obstacle.HalfX);
            if (c != 0) return c;
            c = a.Obstacle.HalfZ.CompareTo(b.Obstacle.HalfZ);
            if (c != 0) return c;
            c = a.Obstacle.Height.CompareTo(b.Obstacle.Height);
            return c != 0 ? c : ((byte)a.Obstacle.Surface).CompareTo((byte)b.Obstacle.Surface);
        }

        private static int CompareSpawns((string Name, SimVector3 Pos) a, (string Name, SimVector3 Pos) b)
        {
            int c = string.CompareOrdinal(a.Name, b.Name);
            if (c != 0) return c;
            c = a.Pos.X.CompareTo(b.Pos.X);
            if (c != 0) return c;
            c = a.Pos.Y.CompareTo(b.Pos.Y);
            return c != 0 ? c : a.Pos.Z.CompareTo(b.Pos.Z);
        }

        // ---- 产物 JSON（Luban 数据源：单文件＝单对象；确定性文本）----

        private static string BuildJson(int mapId, string mapName, string digest, SimMapData map)
        {
            var sb = new StringBuilder(256 + map.ObstacleCount * 96 + map.SpawnPointCount * 40);
            sb.Append("{\n");
            sb.Append("  \"map_id\": ").Append(mapId).Append(",\n");
            sb.Append("  \"name\": \"").Append(mapName).Append("\",\n");
            sb.Append("  \"content_digest\": \"").Append(digest).Append("\",\n");
            sb.Append("  \"ground_y\": ").Append(SimMapDigest.Fmt(map.GroundY)).Append(",\n");
            sb.Append("  \"half_width\": ").Append(SimMapDigest.Fmt(map.HalfWidth)).Append(",\n");
            sb.Append("  \"half_depth\": ").Append(SimMapDigest.Fmt(map.HalfDepth)).Append(",\n");

            sb.Append("  \"obstacles\": [");
            for (int i = 0; i < map.ObstacleCount; i++)
            {
                ref SimObstacle o = ref map.Obstacles[i];
                sb.Append("\n    {\"kind\": ").Append((int)o.Kind)
                  .Append(", \"x\": ").Append(SimMapDigest.Fmt(o.Center.X))
                  .Append(", \"y\": ").Append(SimMapDigest.Fmt(o.Center.Y))
                  .Append(", \"z\": ").Append(SimMapDigest.Fmt(o.Center.Z))
                  .Append(", \"radius\": ").Append(SimMapDigest.Fmt(o.Radius))
                  .Append(", \"half_x\": ").Append(SimMapDigest.Fmt(o.HalfX))
                  .Append(", \"half_z\": ").Append(SimMapDigest.Fmt(o.HalfZ))
                  .Append(", \"height\": ").Append(SimMapDigest.Fmt(o.Height))
                  .Append(", \"surface\": ").Append((int)o.Surface).Append('}');
                if (i < map.ObstacleCount - 1) sb.Append(',');
            }
            sb.Append("\n  ],\n");

            sb.Append("  \"spawns\": [");
            for (int i = 0; i < map.SpawnPointCount; i++)
            {
                ref SimVector3 p = ref map.SpawnPoints[i];
                sb.Append("\n    {\"x\": ").Append(SimMapDigest.Fmt(p.X))
                  .Append(", \"y\": ").Append(SimMapDigest.Fmt(p.Y))
                  .Append(", \"z\": ").Append(SimMapDigest.Fmt(p.Z)).Append('}');
                if (i < map.SpawnPointCount - 1) sb.Append(',');
            }
            sb.Append("\n  ]\n}\n");
            return sb.ToString();
        }

        /// <summary>障碍草稿（采集名 ＋ 量化后值 ＋ 占位面积——超上限选择依据）。</summary>
        private readonly struct ObstacleDraft
        {
            public readonly string Name;
            public readonly SimObstacle Obstacle;
            public readonly float Footprint;

            public ObstacleDraft(string name, SimObstacle obstacle, float footprint)
            {
                Name = name;
                Obstacle = obstacle;
                Footprint = footprint;
            }
        }
    }
}
