using System.Collections.Generic;
using LiteClient.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.EditorTools.UI
{
    /// <summary>
    /// Selectable 状态色接缝的编辑期迁移件（P2-3 B4）：只给直挂 ColorTint Selectable 补 `UiSelectableTheme`，
    /// 跳过嵌套 prefab 实例（由源模板继承）；只加组件、不改任何颜色/ColorBlock 序列化值；幂等。
    /// </summary>
    public static class UiSelectableThemeApplier
    {
        /// <summary>迁移报告（Added=新增组件；Direct=直挂有组件；Inherited=嵌套实例跳过；Skipped=非 ColorTint）。</summary>
        public sealed class EnsureReport
        {
            public int Prefabs;
            public int Added;
            public int Direct;
            public int Inherited;
            public int Skipped;
        }

        /// <summary>迁移默认扫描面：控件模板 + 页面 + HUD（与校验核心同三面）。</summary>
        public static readonly string[] DefaultScanDirs =
        {
            "Assets/ShareResource/UI/Widgets",
            "Assets/ShareResource/UI/Screens",
            "Assets/ShareResource/UI/Hud",
        };

        /// <summary>按目录补组件（幂等）；真实面预期 Added=33、Direct=33、Inherited=13。</summary>
        public static EnsureReport EnsureAll(IReadOnlyList<string> scanDirs = null)
        {
            var report = new EnsureReport();
            foreach (string path in PrefabPaths(scanDirs ?? DefaultScanDirs))
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    bool dirty = false;
                    foreach (Selectable selectable in root.GetComponentsInChildren<Selectable>(true))
                    {
                        if (selectable.transition != Selectable.Transition.ColorTint) { report.Skipped++; continue; }
                        if (PrefabUtility.IsPartOfPrefabInstance(selectable)) { report.Inherited++; continue; }
                        if (selectable.GetComponent<UiSelectableTheme>() != null) { report.Direct++; continue; }
                        selectable.gameObject.AddComponent<UiSelectableTheme>();
                        report.Added++;
                        report.Direct++;
                        dirty = true;
                    }

                    if (dirty)
                    {
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                        report.Prefabs++;
                    }
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }

            if (report.Prefabs > 0) AssetDatabase.SaveAssets();
            Debug.Log($"[UiSelectableThemeApplier] 补组件：{report.Prefabs} prefab / 新增 {report.Added}"
                      + $"（直挂 {report.Direct}、嵌套继承 {report.Inherited}、非 ColorTint {report.Skipped}）");
            return report;
        }

        private static IEnumerable<string> PrefabPaths(IReadOnlyList<string> scanDirs)
        {
            var paths = new List<string>();
            foreach (string dir in scanDirs)
            {
                if (!AssetDatabase.IsValidFolder(dir)) continue;
                foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { dir }))
                    paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            }
            paths.Sort(System.StringComparer.Ordinal);
            return paths;
        }
    }
}