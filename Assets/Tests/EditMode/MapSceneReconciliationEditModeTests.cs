using System.Collections.Generic;
using System.IO;
using System.Text;
using cfg;
using LiteClient;
using LiteSim;
using Luban;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 场景采样对账（《地图数据专项设计》§9/批5）：首图场景 `Arena01` 的 `MapCollision` 代理树（物理事实）
    /// 与 `tbmap.bytes` 投影出的判定图（SimRaycast 的输入）**同命中、同距离**——守"视觉能穿、判定挡住"
    /// 类漂移（改场景忘重烘 → 判定墙与视觉墙错位）。
    ///
    /// 采样：确定性 RNG（固定种子）四高度带水平射线（0.5/1.5/2.2/3.0——覆盖 1m 低箱/2.5m 高墙/20m 围墙
    /// 三档语义）；原点域内随机、重采至不落任意代理内；物理命中按**代理集过滤**（Environment 等场景件不算）。
    /// 容差：命中距离差 ≤ 0.01m（同批几何按毫米量化精确落位，正常应近零；非零大值＝重烘缺失）。
    /// </summary>
    [Category(LiteTesting.TestCategory.Contract)]
    public sealed class MapSceneReconciliationEditModeTests
    {
        private const string ScenePath = "Assets/Scenes/Arena01.unity";
        private const string TablePath = "Assets/GameData/Config/Map/tbmap.bytes";
        private const int SampleCount = 480;
        private const float MaxRange = 60f;
        private const float ToleranceMeters = 0.01f;

        [Test]
        public void 场景对账_判定与物理同命中同距离()
        {
            // 判定图：与线上同一份 bytes 经同一共享投影（服务端 ServerMapLoader／客户端 MapProvider 同源）
            byte[] bytes = File.ReadAllBytes(TablePath);
            var table = new Tbmap(new ByteBuf(bytes));
            SimMapData map = MapTableProjection.Project(table.GetOrDefault(MapTableProjection.DefaultMapId));

            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedHere = !(scene.IsValid() && scene.isLoaded);
            if (openedHere) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                GameObject root = null;
                foreach (GameObject go in scene.GetRootGameObjects())
                {
                    if (go.name == "MapCollision") { root = go; break; }
                }
                Assert.IsNotNull(root, $"场景缺 MapCollision 根：{ScenePath}");

                var proxies = new HashSet<Collider>(root.GetComponentsInChildren<Collider>());
                Assert.AreEqual(map.ObstacleCount, proxies.Count,
                    "代理件数 ≠ 障碍数——场景改动未重烘（工具箱「地图」页 → gen.bat）");
                Physics.SyncTransforms();

                var rng = new System.Random(20261014);
                float[] heights = { 0.5f, 1.5f, 2.2f, 3.0f };
                var hits = 0;
                var mismatches = new StringBuilder();

                for (int i = 0; i < SampleCount; i++)
                {
                    Vector3 origin;
                    int guard = 0;
                    do
                    {
                        origin = new Vector3(Rand(rng, -65f, 65f), heights[i % heights.Length], Rand(rng, -65f, 65f));
                    } while (InsideAny(proxies, origin) && ++guard < 100);

                    var dir = Quaternion.Euler(0f, Rand(rng, 0f, 360f), 0f) * Vector3.forward;

                    bool simHit = SimRaycast.RaycastObstacles(map, origin.x, origin.y, origin.z,
                        dir.x, dir.z, MaxRange, out float simT);
                    bool physHit = TryProxyRaycast(proxies, origin, dir, out float physT);
                    if (simHit) hits++;

                    bool mismatch;
                    string kind;
                    if (simHit && physHit)
                    {
                        mismatch = Mathf.Abs(simT - physT) > ToleranceMeters;
                        kind = $"距离差 {simT:F3} vs {physT:F3}";
                    }
                    else if (simHit != physHit)
                    {
                        mismatch = true;
                        kind = simHit ? "判定命中/物理未中" : "物理命中/判定未中";
                    }
                    else
                    {
                        continue;
                    }

                    if (mismatch && mismatches.Length < 800)
                        mismatches.Append($"\n  [{i}] o=({origin.x:F2},{origin.y:F2},{origin.z:F2}) d=({dir.x:F3},{dir.z:F3}) {kind}");
                }

                Assert.AreEqual(0, mismatches.Length, $"判定/物理不符（采样 {SampleCount} 条）：{mismatches}");
                Assert.Greater(hits, 50, "采样命中过少——采样面可能失效（场景空/图空）");
            }
            finally
            {
                if (openedHere) EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static bool TryProxyRaycast(HashSet<Collider> proxies, Vector3 origin, Vector3 dir, out float distance)
        {
            var buffer = new RaycastHit[16];
            int count = Physics.RaycastNonAlloc(origin, dir, buffer, MaxRange);
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (proxies.Contains(buffer[i].collider) && buffer[i].distance < best)
                    best = buffer[i].distance;
            }
            distance = best;
            return best < float.MaxValue;
        }

        private static bool InsideAny(HashSet<Collider> proxies, Vector3 p)
        {
            foreach (Collider c in proxies)
            {
                if (c.bounds.Contains(p)) return true;
            }
            return false;
        }

        private static float Rand(System.Random rng, float min, float max)
            => min + (float)rng.NextDouble() * (max - min);
    }
}
