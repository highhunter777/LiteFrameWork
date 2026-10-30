using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using LiteGame.Modules.Map;

namespace LiteGame.EditorTools
{
    /// <summary>
    /// 训练场布点工具（多图批；第三批 III-b 的一次性构建器 <c>AgentScripts/BuildTrainingGroundMap.cs</c>
    /// **版本化迁入本件**——《地图数据专项设计》地图场景约定）。布点即内容：掩体代理 ＋ `spawn_NN` ＋
    /// `map_bounds` 都由本件建/补，改布点有两条路：
    ///
    /// - **手工调（首选）**：在场景里拖动/增删 `spawn_NN`、改代理位置尺寸（烘焙器判据：Box/Sphere/Capsule、
    ///   正缩放、yaw 仅 90° 倍数、禁 MeshCollider）——本件的 <see cref="EnsureSeed"/> **只补缺失件、不删已有**，
    ///   手工布点不会被重跑抹掉；
    /// - **回到种子**：<see cref="RebuildSeed"/> 显式全量重建（清旧 `MapCollision` 重建种子布点——原
    ///   <c>All()</c> 语义，"从零来一遍"用）。
    ///
    /// 两条路之后都要走发布链（布点改动＝判定数据改动）：烘焙（烘焙工具箱「地图」页 `SimMapBaker.AllTraining`）
    /// → `Luban/gen.bat` → `python scripts/codegen/gen-build-hash.py`。烘焙器对布点做 fail-fast 校验
    /// （出生点不落障碍内/不越边界、数量 1..16——调点打偏即刻在报告里点名）。
    /// </summary>
    public static class TrainingGroundLayout
    {
        /// <summary>种子出生点数（环形均布；上限 = 判定图 `MaxSpawnPoints`=16）。</summary>
        public const int SpawnCount = 16;

        /// <summary>种子出生环半径（在种子掩体之外；按场地 50×50 边界内）。</summary>
        public const float SpawnRadius = 22f;

        /// <summary>种子边界半宽/半深（`map_bounds`；覆盖全部种子布点）。</summary>
        public const float SeedHalfExtent = 50f;

        /// <summary>补齐缺失件（**非破坏**）：已有件一律不动——手工调过的布点是场景事实，重跑不抹。</summary>
        public static void EnsureSeedMenu() => Debug.Log("[TrainingGroundLayout]\n" + EnsureSeed());

        /// <summary>全量重建种子布点（清旧 MapCollision——"回到种子"用；幂等）。</summary>
        public static void RebuildSeedMenu() => Debug.Log("[TrainingGroundLayout]\n" + RebuildSeed());

        /// <summary>补齐缺失件入口（工具箱/run_script 可直调；返回多行报告）。</summary>
        public static string EnsureSeed() => Build(force: false);

        /// <summary>全量重建入口（工具箱/run_script 可直调；返回多行报告）。</summary>
        public static string RebuildSeed() => Build(force: true);

        private static string Build(bool force)
        {
            string scenePath = BakeTargets.TrainingMap.ScenePath;      // 场景路径单源（BakeTargets）
            var sb = new StringBuilder();
            var markerType = System.Type.GetType("LiteGame.MapBoundsMarker, LiteGame.App");
            if (markerType == null) return "ABORT MapBoundsMarker 未编译（LiteGame.App 未加载）";

            Scene scene = SceneManager.GetSceneByPath(scenePath);
            bool openedHere = false;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                openedHere = true;
            }
            EditorSceneManager.SetActiveScene(scene);
            try
            {
                Transform root = FindRoot(scene, BakeTargets.MapCollisionRoot);
                if (root != null && force)
                {
                    UnityEngine.Object.DestroyImmediate(root.gameObject);
                    sb.AppendLine("旧 MapCollision 已清（全量重建）");
                    root = null;
                }
                if (root == null)
                {
                    root = new GameObject(BakeTargets.MapCollisionRoot).transform;
                    sb.AppendLine("MapCollision 根已建");
                }
                else
                {
                    sb.AppendLine("MapCollision 已存在（保留既有布点——手工调整不被重跑抹掉）");
                }

                EnsureCovers(root, sb);
                EnsureCubeCover(root, scene, sb);
                EnsureSpawns(root, sb);
                EnsureBounds(root, markerType, sb);

                sb.Append("下一步：烘焙（烘焙工具箱「地图」页 → 训练场烘焙）→ Luban/gen.bat → gen-build-hash.py");
                EditorSceneManager.MarkSceneDirty(scene);
                sb.AppendLine();
                sb.Append(EditorSceneManager.SaveScene(scene) ? "SCENE saved" : "ABORT SaveScene 失败");
            }
            finally
            {
                if (openedHere) EditorSceneManager.CloseScene(scene, true);
            }
            return sb.ToString();
        }

        private static Transform FindRoot(Scene scene, string name)
        {
            foreach (GameObject rootObj in scene.GetRootGameObjects())
                if (rootObj.name == name) return rootObj.transform;
            return null;
        }

        /// <summary>种子掩体代理（yaw=0；正缩放；primitive 自带 BoxCollider）——缺任何代理件时补齐一组。</summary>
        private static void EnsureCovers(Transform root, StringBuilder sb)
        {
            int existing = 0;
            for (int i = 0; i < root.childCount; i++)
            {
                string n = root.GetChild(i).name;
                if (n.StartsWith("cover_", System.StringComparison.Ordinal)
                    || n.StartsWith("block_", System.StringComparison.Ordinal)
                    || n.StartsWith("wall_", System.StringComparison.Ordinal)) existing++;
            }
            if (existing > 0)
            {
                sb.AppendLine($"掩体代理已在（{existing} 件，保留）");
                return;
            }
            Box(root, "cover_00", new Vector3(-6f, 0.5f, -6f), new Vector3(2f, 1f, 2f));
            Box(root, "cover_01", new Vector3(6f, 0.5f, -6f), new Vector3(2f, 1f, 2f));
            Box(root, "cover_02", new Vector3(-6f, 0.5f, 6f), new Vector3(2f, 1f, 2f));
            Box(root, "cover_03", new Vector3(6f, 0.5f, 6f), new Vector3(2f, 1f, 2f));
            Box(root, "block_00", new Vector3(0f, 1f, 0f), new Vector3(1f, 2f, 4f));
            Box(root, "wall_00", new Vector3(-14f, 0.5f, 0f), new Vector3(8f, 1f, 1f));
            Box(root, "wall_01", new Vector3(14f, 0.5f, 0f), new Vector3(8f, 1f, 1f));
            sb.AppendLine("掩体代理 7 件已建（种子）");
        }

        /// <summary>场景原顶层 Cube：归入采集根当一件掩体（否则"看得见但判定里没有"）。</summary>
        private static void EnsureCubeCover(Transform root, Scene scene, StringBuilder sb)
        {
            foreach (GameObject rootObj in scene.GetRootGameObjects())
            {
                if (rootObj.name != "Cube") continue;
                rootObj.name = "cover_04";
                rootObj.transform.SetParent(root, true);
                sb.AppendLine("原 Cube → cover_04（进采集根）");
                return;
            }
        }

        /// <summary>种子出生标记（不挂碰撞体；按名排序定槽位 spawn_00..NN）——已有出生点则保留。</summary>
        private static void EnsureSpawns(Transform root, StringBuilder sb)
        {
            int existing = 0;
            for (int i = 0; i < root.childCount; i++)
                if (root.GetChild(i).name.StartsWith(BakeTargets.SpawnPrefix, System.StringComparison.Ordinal)) existing++;
            if (existing > 0)
            {
                sb.AppendLine($"出生标记已在（{existing} 件，保留——挪点直接拖对象）");
                return;
            }
            for (int i = 0; i < SpawnCount; i++)
            {
                float angle = Mathf.PI * 2f * i / SpawnCount;
                var spawn = new GameObject($"spawn_{i:00}");
                spawn.transform.SetParent(root, false);
                spawn.transform.position = new Vector3(Mathf.Cos(angle) * SpawnRadius, 0f, Mathf.Sin(angle) * SpawnRadius);
            }
            sb.AppendLine($"出生标记 {SpawnCount} 件已建（种子环，半径 {SpawnRadius}）");
        }

        /// <summary>边界标注（唯一；禁挂碰撞体）——已有则保留（边界值可在 Inspector 改）。</summary>
        private static void EnsureBounds(Transform root, System.Type markerType, StringBuilder sb)
        {
            Transform bounds = BakeTargets.FindDeep(root, BakeTargets.MapBoundsObject);
            if (bounds != null)
            {
                sb.AppendLine($"{BakeTargets.MapBoundsObject} 已在（保留；值为 {DescribeBounds(bounds)}）");
                return;
            }
            var obj = new GameObject(BakeTargets.MapBoundsObject);
            obj.transform.SetParent(root, false);
            Component marker = obj.AddComponent(markerType);
            var so = new SerializedObject(marker);
            so.FindProperty("GroundY").floatValue = 0f;
            so.FindProperty("HalfWidth").floatValue = SeedHalfExtent;
            so.FindProperty("HalfDepth").floatValue = SeedHalfExtent;
            so.ApplyModifiedPropertiesWithoutUndo();
            sb.AppendLine($"{BakeTargets.MapBoundsObject} 已建（种子：GroundY=0 / {SeedHalfExtent}×{SeedHalfExtent}）");
        }

        private static string DescribeBounds(Transform bounds)
        {
            Component marker = bounds.GetComponent("MapBoundsMarker");
            if (marker == null) return "无 MapBoundsMarker 组件（烘焙会拒载）";
            var so = new SerializedObject(marker);
            return "GroundY=" + so.FindProperty("GroundY").floatValue
                + " HalfWidth=" + so.FindProperty("HalfWidth").floatValue
                + " HalfDepth=" + so.FindProperty("HalfDepth").floatValue;
        }

        private static void Box(Transform parent, string name, Vector3 pos, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);     // 自带 MeshRenderer + BoxCollider
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = size;                              // 正缩放、无旋转——烘焙器判据
        }
    }
}
