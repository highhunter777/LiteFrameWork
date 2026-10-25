using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.EditorTools.UI
{
    /// <summary>
    /// 样式工具（"统一色板"的落地执行件；**仅编辑器批量工具**）。
    ///
    /// 机制：**最近 token 匹配 + 阈值**——每个 Graphic 的颜色与 token 全集比对，落在阈值内即视为该 token
    /// 的实例，刷新时改写为 token 现值。由此获得两个关键性质：
    ///   1. **可重入**：颜色已在 token 值上 → 最近匹配即自身 → 幂等；
    ///   2. **改值可传播**：改 UiStyle 的 token 值后重跑刷新，旧值落在最近邻阈值内 → 全量迁移到新值。
    /// 阈值 1e-4（逐通道差平方和）——只吸收浮点噪声与"同一意图的离散变体"，手调出的新色不受影响，
    /// 由"对账"报告暴露。
    ///
    /// - 刷新（ApplyColors）：把扫描面（Widgets + Screens）prefab 中**命中 token** 的 Graphic 颜色改写为 token 现值。
    /// - 对账（ReportDrift）：报告**不命中任何 token** 的颜色（手调/漂移检测）。
    /// - 菜单：LiteGame/UI/样式工具/刷新 UI 颜色（按 token 收敛）、LiteGame/UI/样式工具/对账 UI 颜色（漂移报告）。
    ///
    /// 边界：作用域＝共享资源层的控件模板与页面夹具（§3.2）；不挂组件、不进运行时、不改结构；
    /// 手作模板为唯一维护路径。
    /// </summary>
    public static class UiStyleTool
    {
        /// <summary>颜色对账作用域（§3.2——控件模板 + 页面夹具；扫描根可注入，测试用）。</summary>
        public static readonly string[] DefaultScanDirs =
        {
            "Assets/ShareResource/UI/Widgets",
            "Assets/ShareResource/UI/Screens",
        };

        /// <summary>命中阈值：颜色到 token 的平方距离 ≤ 此值视为该 token 的实例（吸收浮点噪声与离散变体）。</summary>
        private const float HitEpsilon = 1e-4f;

        public static void ApplyColors() => ApplyColors(DefaultScanDirs);

        /// <summary>刷新（扫描根可注入）：命中 token 的 Graphic 颜色改写为 token 现值。返回 (改动 prefab 数, 改动颜色数)。</summary>
        public static (int prefabs, int graphics) ApplyColors(IReadOnlyList<string> scanDirs)
            => ApplyColorsWith(UiTokenCatalog.Load(), scanDirs);

        /// <summary>刷新（给定目录；工具一次装载、逐 prefab 复用，不逐节点重读资产）。</summary>
        public static (int prefabs, int graphics) ApplyColorsWith(UiTokenCatalog catalog, IReadOnlyList<string> scanDirs)
        {
            if (catalog == null) throw new System.ArgumentNullException(nameof(catalog));
            var log = new StringBuilder();
            int changedPrefabs = 0, changedGraphics = 0;

            foreach (string path in PrefabPaths(scanDirs))
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                bool dirty = false;
                int count = 0;

                foreach (var graphic in root.GetComponentsInChildren<Graphic>(true))
                {
                    if (TryMatch(graphic.color, catalog, out Color value) && UiStyle.Distance(graphic.color, value) > 0f)
                    {
                        graphic.color = value;
                        dirty = true;
                        count++;
                    }
                }

                if (dirty)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    changedPrefabs++;
                    changedGraphics += count;
                    log.AppendLine($"{System.IO.Path.GetFileName(path)}: {count} 处");
                }
                PrefabUtility.UnloadPrefabContents(root);
            }

            if (changedPrefabs > 0) AssetDatabase.SaveAssets();
            Debug.Log($"[UiStyleTool] 刷新完成：{changedPrefabs} 个 prefab / {changedGraphics} 处颜色收敛到 token 现值\n{log}");
            return (changedPrefabs, changedGraphics);
        }

        public static void MenuReportDrift()
        {
            var drifts = ReportDrift(DefaultScanDirs);
            if (drifts.Count == 0)
            {
                Debug.Log("[UiStyleTool] 对账通过：扫描面颜色全部命中 token 集（零漂移基线成立）");
                return;
            }
            var log = new StringBuilder();
            foreach (var (asset, node, color) in drifts)
                log.AppendLine($"{System.IO.Path.GetFileName(asset)} :: {node} = {color:F3}（不在 token 集——手调值或漂移，仅报告不擅改）");
            Debug.LogWarning($"[UiStyleTool] 漂移 {drifts.Count} 处（手调值不擅改，仅报告）：\n{log}");
        }

        /// <summary>颜色对账（扫描根可注入）：返回不命中 token 集的颜色（手调值或漂移——报告不擅改）。</summary>
        public static List<(string asset, string node, Color color)> ReportDrift(IReadOnlyList<string> scanDirs)
            => ReportDriftWith(UiTokenCatalog.Load(), scanDirs);

        /// <summary>颜色对账（给定目录；工具一次装载）。</summary>
        public static List<(string asset, string node, Color color)> ReportDriftWith(
            UiTokenCatalog catalog, IReadOnlyList<string> scanDirs)
        {
            if (catalog == null) throw new System.ArgumentNullException(nameof(catalog));
            var drifts = new List<(string, string, Color)>();
            foreach (string path in PrefabPaths(scanDirs))
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    foreach (var graphic in root.GetComponentsInChildren<Graphic>(true))
                        if (!TryMatch(graphic.color, catalog, out _))
                            drifts.Add((path, graphic.transform.name, graphic.color));
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            return drifts;
        }

        // ---- 辅助 ----

        /// <summary>最近 token 匹配（阈值内）；命中返回 token 现值。</summary>
        private static bool TryMatch(Color color, UiTokenCatalog catalog, out Color value)
        {
            value = default;
            bool hit = false;
            float best = HitEpsilon;
            foreach (var pair in catalog.Colors)
            {
                float d = UiStyle.Distance(color, pair.Value);
                if (d <= best)
                {
                    best = d;
                    value = pair.Value;
                    hit = true;
                }
            }
            return hit;
        }

        private static IEnumerable<string> PrefabPaths(IReadOnlyList<string> scanDirs)
        {
            var dirs = scanDirs ?? DefaultScanDirs;
            var paths = new List<string>();
            foreach (var dir in dirs)
            {
                if (!AssetDatabase.IsValidFolder(dir)) continue;
                var guids = AssetDatabase.FindAssets("t:Prefab", new[] { dir });
                foreach (var guid in guids) paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            }
            paths.Sort(System.StringComparer.Ordinal);
            return paths;
        }
    }
}
