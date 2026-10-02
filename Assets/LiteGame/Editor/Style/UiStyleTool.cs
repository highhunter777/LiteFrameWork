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
    /// - 刷新（ApplyColors）：把 25 件控件模板 prefab 中**命中 token** 的 Graphic 颜色改写为 token 现值。
    /// - 对账（ReportDrift）：报告**不命中任何 token** 的颜色（手调/漂移检测）。
    /// - 菜单：LiteGame/UI/样式工具/刷新模板颜色（按主题）、LiteGame/UI/样式工具/对账模板颜色（漂移报告）。
    ///
    /// 边界：只动 Assets/UI/Widgets/ 下的模板 prefab；不挂组件、不进运行时、不改结构；
    /// 手作模板为唯一维护路径。
    /// </summary>
    public static class UiStyleTool
    {
        private const string WidgetsDir = "Assets/UI/Widgets";  // UI 控件模板目录（顶层 Assets/UI）

        /// <summary>命中阈值：颜色到 token 的平方距离 ≤ 此值视为该 token 的实例（吸收浮点噪声与离散变体）。</summary>
        private const float HitEpsilon = 1e-4f;

        [MenuItem("LiteGame/UI/样式工具/刷新模板颜色（按 token 收敛）")]
        public static void ApplyColors()
        {
            var log = new StringBuilder();
            int changedPrefabs = 0, changedGraphics = 0;

            foreach (string path in PrefabPaths())
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                bool dirty = false;
                int count = 0;

                foreach (var graphic in root.GetComponentsInChildren<Graphic>(true))
                {
                    if (TryMatch(graphic.color, out Color value) && UiStyle.Distance(graphic.color, value) > 0f)
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

            Debug.Log($"[UiStyleTool] 刷新完成：{changedPrefabs} 个模板 / {changedGraphics} 处颜色收敛到 token 现值\n{log}");
            AssetDatabase.SaveAssets();
        }

        [MenuItem("LiteGame/UI/样式工具/对账模板颜色（漂移报告）")]
        public static void ReportDrift()
        {
            var log = new StringBuilder();
            int drift = 0;

            foreach (string path in PrefabPaths())
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                foreach (var graphic in root.GetComponentsInChildren<Graphic>(true))
                {
                    if (!TryMatch(graphic.color, out _))
                    {
                        drift++;
                        log.AppendLine($"{System.IO.Path.GetFileName(path)} :: {graphic.transform.name} = {graphic.color:F3}（不在 token 集——手调值或漂移，仅报告不擅改）");
                    }
                }
                PrefabUtility.UnloadPrefabContents(root);
            }

            if (drift == 0) Debug.Log("[UiStyleTool] 对账通过：全部模板颜色都命中 token 集（零漂移基线成立）");
            else Debug.LogWarning($"[UiStyleTool] 漂移 {drift} 处（手调值不擅改，仅报告）：\n{log}");
        }

        // ---- 辅助 ----

        /// <summary>最近 token 匹配（阈值内）；命中返回 token 现值。</summary>
        private static bool TryMatch(Color color, out Color value)
        {
            value = default;
            bool hit = false;
            float best = HitEpsilon;
            foreach (var (_, tokenValue) in UiStyle.Tokens)
            {
                float d = UiStyle.Distance(color, tokenValue);
                if (d <= best)
                {
                    best = d;
                    value = tokenValue;
                    hit = true;
                }
            }
            return hit;
        }

        private static IEnumerable<string> PrefabPaths()
        {
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { WidgetsDir });
            var paths = new List<string>(guids.Length);
            foreach (var guid in guids) paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            paths.Sort(System.StringComparer.Ordinal);
            return paths;
        }
    }
}
