using System.Collections.Generic;
using LiteFramework.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.EditorTools.UI
{
    /// <summary>
    /// 引用声明应用核心（《UI 编辑器工具链专项设计》§8.1「刷新/应用：按令牌应用；幂等、报告」）。
    /// </summary>
    public static class UIStyleRefApplier
    {
        /// <summary>refs 应用报告（refs 段）。</summary>
        public sealed class RefsReport
        {
            /// <summary>有改动的 prefab 数。</summary>
            public int Prefabs;

            /// <summary>实际写值数（值已在令牌现值上不计——幂等）。</summary>
            public int Applied;

            /// <summary>跳过数（空声明/无 Graphic/IThemeBound 双写者节点）。</summary>
            public int Skipped;

            /// <summary>未知令牌数（不猜色，规则报 Error）。</summary>
            public int UnknownToken;
        }

        /// <summary>widget 状态色迁移报告（迁移段）。</summary>
        public sealed class MigrationReport
        {
            /// <summary>有改动的 prefab 数。</summary>
            public int Prefabs;

            /// <summary>填入 token ID 的字段数。</summary>
            public int Filled;

            /// <summary>颜色不命中任何令牌的字段数（留空合法）。</summary>
            public int Missed;
        }

        /// <summary>按声明应用 refs（扫描目录可注入；幂等——值已在令牌现值上则 Applied=0）。</summary>
        public static RefsReport ApplyRefs(UITokenCatalog catalog, IReadOnlyList<string> scanDirs)
        {
            if (catalog == null) throw new System.ArgumentNullException(nameof(catalog));
            var report = new RefsReport();

            foreach (string path in PrefabPaths(scanDirs))
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    bool dirty = false;
                    foreach (UIStyleRef styleRef in root.GetComponentsInChildren<UIStyleRef>(true))
                    {
                        // 写者裁决：writer 自持其自身/子树 Graphic——refs 不越权（双写者由规则报 Error）
                        if (UIThemeBinder.IsGraphicOwnedByWidget(styleRef)) { report.Skipped++; continue; }
                        string token = styleRef.ColorToken;
                        if (string.IsNullOrEmpty(token)) { report.Skipped++; continue; }
                        if (!catalog.TryColor(token, out Color value)) { report.UnknownToken++; continue; }
                        Graphic target = styleRef.GetComponent<Graphic>();
                        if (target == null) { report.Skipped++; continue; }
                        if (UIStyle.Distance(target.color, value) > 0f)
                        {
                            target.color = value;
                            dirty = true;
                            report.Applied++;
                        }
                    }

                    if (dirty)
                    {
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                        report.Prefabs++;
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            if (report.Prefabs > 0) AssetDatabase.SaveAssets();
            Debug.Log($"[UIStyleRefApplier] refs 应用：{report.Prefabs} prefab / 写值 {report.Applied}"
                      + $"（跳过 {report.Skipped}、未知令牌 {report.UnknownToken}）");
            return report;
        }

        /// <summary>widget 组件的状态色字段访问面（非三 widget 返回空）。</summary>
        public static (string Field, System.Func<Color> Color, System.Func<string> Token, System.Action<string> SetToken)[] WidgetFields(Component component)
        {
            switch (component)
            {
                case StateButton b:
                    return new[]
                    {
                        (nameof(b.NormalColor), (System.Func<Color>)(() => b.NormalColor), (System.Func<string>)(() => b.NormalColorToken), (System.Action<string>)(v => b.NormalColorToken = v)),
                        (nameof(b.PressedColor), () => b.PressedColor, () => b.PressedColorToken, v => b.PressedColorToken = v),
                        (nameof(b.DisabledColor), () => b.DisabledColor, () => b.DisabledColorToken, v => b.DisabledColorToken = v),
                        (nameof(b.SelectedColor), () => b.SelectedColor, () => b.SelectedColorToken, v => b.SelectedColorToken = v),
                    };
                case TabGroup t:
                    return new[]
                    {
                        (nameof(t.SelectedColor), (System.Func<Color>)(() => t.SelectedColor), (System.Func<string>)(() => t.SelectedColorToken), (System.Action<string>)(v => t.SelectedColorToken = v)),
                        (nameof(t.NormalColor), () => t.NormalColor, () => t.NormalColorToken, v => t.NormalColorToken = v),
                    };
                case StarRating s:
                    return new[]
                    {
                        (nameof(s.OnColor), (System.Func<Color>)(() => s.OnColor), (System.Func<string>)(() => s.OnColorToken), (System.Action<string>)(v => s.OnColorToken = v)),
                        (nameof(s.OffColor), () => s.OffColor, () => s.OffColorToken, v => s.OffColorToken = v),
                    };
                default:
                    return System.Array.Empty<(string, System.Func<Color>, System.Func<string>, System.Action<string>)>();
            }
        }

        /// <summary>三 widget 状态色字段按最近匹配填 token ID（已填跳过＝幂等；不命中不填）。</summary>
        public static MigrationReport MigrateWidgetTokens(UITokenCatalog catalog, IReadOnlyList<string> scanDirs)
        {
            if (catalog == null) throw new System.ArgumentNullException(nameof(catalog));
            var report = new MigrationReport();

            foreach (string path in PrefabPaths(scanDirs))
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    bool dirty = false;
                    foreach (Component component in root.GetComponentsInChildren<Component>(true))
                    {
                        if (!(component is IThemeBound)) continue;
                        foreach (var field in WidgetFields(component))
                        {
                            if (!string.IsNullOrEmpty(field.Token())) continue;      // 已填跳过＝幂等
                            if (UIStyleTool.TryNearestMatch(field.Color(), catalog, out _, out string tokenId,
                                    id => !id.StartsWith(UISelectableTheme.TokenPrefix, System.StringComparison.Ordinal)))
                            {
                                field.SetToken(tokenId);
                                report.Filled++;
                                dirty = true;
                            }
                            else
                            {
                                report.Missed++;
                            }
                        }
                    }

                    if (dirty)
                    {
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                        report.Prefabs++;
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            if (report.Prefabs > 0) AssetDatabase.SaveAssets();
            Debug.Log($"[UIStyleRefApplier] widget 迁移：{report.Prefabs} prefab / 填入 {report.Filled}（未命中 {report.Missed} 留空）");
            return report;
        }

        private static IEnumerable<string> PrefabPaths(IReadOnlyList<string> scanDirs)
        {
            var dirs = scanDirs ?? UIStyleTool.DefaultScanDirs;
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
