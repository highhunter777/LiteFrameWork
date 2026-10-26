using System.Collections.Generic;
using System.Text;
using LiteClient.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.EditorTools.UI
{
    /// <summary>文字档对账的一处问题（《UI编辑器工具专项设计》§3.2 漂移报告分类——当前可实现三类；
    /// "声明覆盖"（偏离基线的合法覆盖）待 SO 覆盖机制随真实需求接入）。</summary>
    public sealed class UiTextIssue
    {
        public string Asset;
        public string Node;
        public UiTextIssueKind Kind;
        public string Detail;
    }

    public enum UiTextIssueKind
    {
        /// <summary>文本组件无 UiTextStyleRef 标记——按整改清单收口，不静默跳过（§3.2）。</summary>
        Unmarked,
        /// <summary>有标记但组件属性 ≠ 角色档基线（文字漂移——报告不擅改，应用按声明收敛）。</summary>
        Mismatch,
        /// <summary>标记失效：节点无 TMP_Text/Text 组件，或 TMP font 资产缺失（资源引用失效）。</summary>
        BrokenRef,

        /// <summary>实例覆盖滥用：UseInstanceOverrides 落在直挂节点（仅嵌套 prefab 实例合法）——Error。</summary>
        OverrideMisuse,
    }

    /// <summary>
    /// 文字样式工具（《UI编辑器工具专项设计》§3.1/§3.2——"统一文字档"的落地执行件；**仅编辑器批量工具**）。
    ///
    /// - **应用（<see cref="ApplyTextStyles"/>）**：按 <see cref="UiTextStyleRef"/> 声明的角色，把档位基线
    ///   **精确写入**文本组件（字号/对齐/换行/初始溢出策略/字重——§3.2"文字枚举/字号精确应用"）；
    ///   值已符合的属性逐项跳过 → 重跑幂等；改基线（<see cref="UiTextStyle"/> 单源）后重跑即全量收敛。
    /// - **对账（<see cref="ReportTextDrift"/>）**：分三类报告——未标记文本（整改清单）、有标记属性漂移、
    ///   标记失效（无文本组件 / TMP font 缺失）。**只读报告不擅改**（对账只读盘上资产——运行时实例变化
    ///   不进资产漂移，§3.2）。
    /// - 作用域与 <see cref="UiStyleTool"/> 同面：Widgets + Screens（§3.2）；扫描根可注入（测试用临时目录）。
    /// </summary>
    public static class UiTextStyleTool
    {
        /// <summary>文字档作用域（§3.2——与颜色对账同面；共享资源层的控件模板与页面夹具）。</summary>
        public static readonly string[] DefaultScanDirs =
        {
            "Assets/ShareResource/UI/Widgets",
            "Assets/ShareResource/UI/Screens",
        };

        public static void MenuApply()
        {
            var (prefabs, writes) = ApplyTextStyles(DefaultScanDirs);
            Debug.Log($"[UiTextStyleTool] 应用完成：{prefabs} 个 prefab / {writes} 处属性收敛到档位现值");
        }

        public static void MenuReport()
        {
            var issues = ReportTextDrift(DefaultScanDirs);
            var log = new StringBuilder();
            int unmarked = 0, mismatch = 0, broken = 0, misuse = 0;
            foreach (var issue in issues)
            {
                switch (issue.Kind)
                {
                    case UiTextIssueKind.Unmarked: unmarked++; break;
                    case UiTextIssueKind.Mismatch: mismatch++; break;
                    case UiTextIssueKind.BrokenRef: broken++; break;
                    case UiTextIssueKind.OverrideMisuse: misuse++; break;
                }
                log.AppendLine($"[{issue.Kind}] {System.IO.Path.GetFileName(issue.Asset)} :: {issue.Node} :: {issue.Detail}");
            }
            Debug.Log($"[UiTextStyleTool] 文字档对账：未标记 {unmarked} · 属性漂移 {mismatch} · 引用失效 {broken} · 实例覆盖滥用 {misuse}\n{log}");
        }

        // ---------------- 应用 ----------------

        /// <summary>把扫描面内所有带 <see cref="UiTextStyleRef"/> 文本的属性收敛到角色档基线（幂等）。
        /// 失效标记（无文本组件/未知角色）跳过不猜——归对账报告。返回 (改动 prefab 数, 写入属性数)。</summary>
        public static (int prefabs, int writes) ApplyTextStyles(IReadOnlyList<string> scanDirs = null)
            => ApplyTextStylesWith(UiTokenCatalog.Load(), scanDirs);

        /// <summary>应用（给定目录；工具一次装载，逐节点复用）。</summary>
        public static (int prefabs, int writes) ApplyTextStylesWith(
            UiTokenCatalog catalog, IReadOnlyList<string> scanDirs = null)
        {
            if (catalog == null) throw new System.ArgumentNullException(nameof(catalog));
            int changedPrefabs = 0, writes = 0;
            var log = new StringBuilder();

            foreach (string path in PrefabPaths(scanDirs))
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                int count = 0;
                try
                {
                    foreach (var reference in root.GetComponentsInChildren<UiTextStyleRef>(true))
                        count += ApplyToRef(reference, catalog);

                    if (count > 0)
                    {
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                        changedPrefabs++;
                        writes += count;
                        log.AppendLine($"{System.IO.Path.GetFileName(path)}: {count} 处");
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            if (changedPrefabs > 0)
            {
                AssetDatabase.SaveAssets();
                Debug.Log($"[UiTextStyleTool] 明细：\n{log}");
            }
            return (changedPrefabs, writes);
        }

        /// <summary>把一个标记的目标属性写为档位基线（逐项比对，值已符合即跳过）。返回写入的属性数。</summary>
        private static int ApplyToRef(UiTextStyleRef reference, UiTokenCatalog catalog)
        {
            var tmp = reference.GetComponent<TMP_Text>();
            var legacy = tmp == null ? reference.GetComponent<Text>() : null;
            if (tmp == null && legacy == null) return 0;         // 失效标记：应用不猜不改，归对账
            if (reference.UseInstanceOverrides) return 0;           // 实例保留自身属性（合法覆盖）
            if (!UiTextStyle.TryGet(reference.Role, catalog, out var style)) return 0;

            int writes = 0;
            if (tmp != null)
            {
                if (!Mathf.Approximately(tmp.fontSize, style.FontSize)) { tmp.fontSize = style.FontSize; writes++; }
                if (tmp.alignment != style.TmpAlignment) { tmp.alignment = style.TmpAlignment; writes++; }
                if (tmp.enableWordWrapping != style.WordWrap) { tmp.enableWordWrapping = style.WordWrap; writes++; }
                var overflow = style.Truncate ? TextOverflowModes.Truncate : TextOverflowModes.Overflow;
                if (tmp.overflowMode != overflow) { tmp.overflowMode = overflow; writes++; }
                var fontStyle = style.Bold ? (tmp.fontStyle | FontStyles.Bold) : (tmp.fontStyle & ~FontStyles.Bold);
                if (tmp.fontStyle != fontStyle) { tmp.fontStyle = fontStyle; writes++; }
            }
            else
            {
                if (legacy.fontSize != (int)style.FontSize) { legacy.fontSize = (int)style.FontSize; writes++; }
                if (legacy.alignment != style.LegacyAlignment) { legacy.alignment = style.LegacyAlignment; writes++; }
                // UGUI 无水平 Truncate 模式（HorizontalWrapMode 只有 Wrap/Overflow）——不换行档映射为
                // Overflow，水平截断由容器/Mask 承担（与 TMP Truncate 档的视觉等价物是容器职责）
                var horizontal = style.WordWrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
                if (legacy.horizontalOverflow != horizontal) { legacy.horizontalOverflow = horizontal; writes++; }
                var fontStyle = style.Bold ? (legacy.fontStyle | FontStyle.Bold) : (legacy.fontStyle & ~FontStyle.Bold);
                if (legacy.fontStyle != fontStyle) { legacy.fontStyle = fontStyle; writes++; }
            }
            return writes;
        }

        // ---------------- 对账 ----------------

        /// <summary>
        /// 对账（只读盘上资产）：每个 TMP_Text/Text 按下述口径各查一遍——
        /// 无标记＝Unmarked；有标记：font 缺失/无文本组件＝BrokenRef；属性 ≠ 档位＝Mismatch（逐项列漂移明细）。
        /// </summary>
        public static List<UiTextIssue> ReportTextDrift(IReadOnlyList<string> scanDirs = null)
            => ReportTextDriftWith(UiTokenCatalog.Load(), scanDirs);

        /// <summary>对账（给定目录；工具一次装载，逐节点复用）。</summary>
        public static List<UiTextIssue> ReportTextDriftWith(
            UiTokenCatalog catalog, IReadOnlyList<string> scanDirs = null)
        {
            if (catalog == null) throw new System.ArgumentNullException(nameof(catalog));
            var issues = new List<UiTextIssue>();
            foreach (string path in PrefabPaths(scanDirs))
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    foreach (var tmp in root.GetComponentsInChildren<TMP_Text>(true))
                        Check(issues, path, tmp.gameObject, tmp, null, catalog);
                    foreach (var legacy in root.GetComponentsInChildren<Text>(true))
                        Check(issues, path, legacy.gameObject, null, legacy, catalog);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            return issues;
        }

        /// <summary>单文本对账（tmp/legacy 二选一非空；TMP 与 UGUI 属性面不同，分别比对）。</summary>
        private static void Check(List<UiTextIssue> issues, string path, GameObject node, TMP_Text tmp, Text legacy,
            UiTokenCatalog catalog)
        {
            var reference = node.GetComponent<UiTextStyleRef>();
            if (reference != null && reference.UseInstanceOverrides)
            {
                if (!PrefabUtility.IsPartOfPrefabInstance(reference))
                {
                    issues.Add(new UiTextIssue
                    {
                        Asset = path, Node = NodePath(node.transform),
                        Kind = UiTextIssueKind.OverrideMisuse,
                        Detail = "UseInstanceOverrides 仅限嵌套 prefab 实例（直挂节点不得关闭角色）",
                    });
                    return;
                }
                issues.Add(new UiTextIssue
                {
                    Asset = path, Node = NodePath(node.transform),
                    Kind = UiTextIssueKind.Unmarked,
                    Detail = "实例属性覆盖（UseInstanceOverrides）——角色不应用，仍留未标记债",
                });
                return;
            }
            if (reference == null)
            {
                issues.Add(new UiTextIssue
                {
                    Asset = path,
                    Node = NodePath(node.transform),
                    Kind = UiTextIssueKind.Unmarked,
                    Detail = "文本组件无 UiTextStyleRef 标记（整改清单收口）",
                });
                return;
            }

            if (tmp != null && tmp.font == null)
            {
                issues.Add(new UiTextIssue
                {
                    Asset = path, Node = NodePath(node.transform),
                    Kind = UiTextIssueKind.BrokenRef, Detail = "TMP font 资产缺失（引用失效）",
                });
                return;
            }
            if (!UiTextStyle.TryGet(reference.Role, catalog, out var style)) return;   // 未知角色：不猜语义

            var drift = new StringBuilder();
            if (tmp != null)
            {
                if (!Mathf.Approximately(tmp.fontSize, style.FontSize))
                    drift.Append($"字号 {tmp.fontSize}≠{style.FontSize} ");
                if (tmp.alignment != style.TmpAlignment)
                    drift.Append($"对齐 {tmp.alignment}≠{style.TmpAlignment} ");
                if (tmp.enableWordWrapping != style.WordWrap)
                    drift.Append($"换行 {tmp.enableWordWrapping}≠{style.WordWrap} ");
                var overflow = style.Truncate ? TextOverflowModes.Truncate : TextOverflowModes.Overflow;
                if (tmp.overflowMode != overflow)
                    drift.Append($"溢出 {tmp.overflowMode}≠{overflow} ");
                bool bold = (tmp.fontStyle & FontStyles.Bold) != 0;
                if (bold != style.Bold) drift.Append($"字重 bold={bold}≠{style.Bold} ");
            }
            else
            {
                if (legacy.fontSize != (int)style.FontSize)
                    drift.Append($"字号 {legacy.fontSize}≠{style.FontSize} ");
                if (legacy.alignment != style.LegacyAlignment)
                    drift.Append($"对齐 {legacy.alignment}≠{style.LegacyAlignment} ");
                var horizontal = style.WordWrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
                if (legacy.horizontalOverflow != horizontal)
                    drift.Append($"水平溢出 {legacy.horizontalOverflow}≠{horizontal} ");
                bool bold = (legacy.fontStyle & FontStyle.Bold) != 0;
                if (bold != style.Bold) drift.Append($"字重 bold={bold}≠{style.Bold} ");
            }
            if (drift.Length > 0)
                issues.Add(new UiTextIssue
                {
                    Asset = path,
                    Node = NodePath(node.transform),
                    Kind = UiTextIssueKind.Mismatch,
                    Detail = drift.ToString().Trim(),
                });
        }

        /// <summary>带 <see cref="UiTextStyleRef"/> 但无文本组件的失效标记（对账补查——应用面跳过项）。</summary>
        public static List<UiTextIssue> ReportBrokenRefs(IReadOnlyList<string> scanDirs = null)
        {
            var issues = new List<UiTextIssue>();
            foreach (string path in PrefabPaths(scanDirs))
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    foreach (var reference in root.GetComponentsInChildren<UiTextStyleRef>(true))
                        if (reference.GetComponent<TMP_Text>() == null && reference.GetComponent<Text>() == null)
                            issues.Add(new UiTextIssue
                            {
                                Asset = path,
                                Node = NodePath(reference.transform),
                                Kind = UiTextIssueKind.BrokenRef,
                                Detail = "标记节点无 TMP_Text/Text 组件",
                            });
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            return issues;
        }

        // ---------------- 精确角色迁移（P2-3 B4） ----------------

        /// <summary>精确角色迁移默认扫描面：控件模板 + 页面 + HUD（37 个匹配中 9 个在 HUD）。</summary>
        private static readonly string[] MigrationScanDirs =
        {
            "Assets/ShareResource/UI/Widgets",
            "Assets/ShareResource/UI/Screens",
            "Assets/ShareResource/UI/Hud",
        };

        /// <summary>精确角色迁移报告（Added=新增组件；Direct=直挂已有/新增；Inherited=嵌套实例跳过；Ambiguous=多档歧义；Skipped=不匹配）。</summary>
        public sealed class TextStyleMigrationReport
        {
            public int Prefabs;
            public int Added;
            public int Direct;
            public int Inherited;
            public int Ambiguous;
            public int Skipped;
            public readonly List<string> Details = new List<string>();
        }

        /// <summary>只给属性与角色档逐项一致、且未标记/非嵌套实例的文本补 `UiTextStyleRef`；只加组件不改属性；幂等。</summary>
        public static TextStyleMigrationReport MigrateExactRoleRefs(UiTokenCatalog catalog, IReadOnlyList<string> scanDirs = null)
        {
            if (catalog == null) throw new System.ArgumentNullException(nameof(catalog));
            var report = new TextStyleMigrationReport();
            UiTextStyleDef[] defs = UiTextStyle.BuildFrom(catalog);

            foreach (string path in PrefabPaths(scanDirs ?? MigrationScanDirs))
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    bool dirty = false;
                    var seen = new HashSet<int>();
                    foreach (TMP_Text tmp in root.GetComponentsInChildren<TMP_Text>(true))
                    {
                        seen.Add(tmp.GetInstanceID());
                        dirty |= MigrateOne(report, defs, path, tmp.gameObject, tmp, null);
                    }
                    foreach (Text legacy in root.GetComponentsInChildren<Text>(true))
                    {
                        if (seen.Contains(legacy.GetInstanceID())) continue;
                        dirty |= MigrateOne(report, defs, path, legacy.gameObject, null, legacy);
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
            Debug.Log($"[UiTextStyleTool] 精确角色迁移：{report.Prefabs} prefab / 新增 {report.Added}"
                      + $"（直挂 {report.Direct}、嵌套跳过 {report.Inherited}、歧义 {report.Ambiguous}、不匹配 {report.Skipped}）");
            return report;
        }

        /// <summary>单节点迁移：嵌套实例跳过（由源模板继承）；已标记跳过；多档歧义跳过并报告；只加组件。</summary>
        private static bool MigrateOne(TextStyleMigrationReport report, UiTextStyleDef[] defs, string path,
            GameObject node, TMP_Text tmp, Text legacy)
        {
            var roles = new List<UiTextRole>();
            foreach (UiTextStyleDef def in defs)
                if (tmp != null ? MatchesTmp(tmp, def) : MatchesLegacy(legacy, def)) roles.Add(def.Role);

            Component component = tmp != null ? (Component)tmp : legacy;
            if (component != null && PrefabUtility.IsPartOfPrefabInstance(component))
            {
                // 嵌套实例不补组件（由源模板继承）；只把「精确匹配的继承节点」计入 Inherited。
                if (roles.Count == 1) report.Inherited++;
                else report.Skipped++;
                return false;
            }
            if (node.GetComponent<UiTextStyleRef>() != null)
            {
                report.Direct++;
                return false;
            }

            if (roles.Count == 0) { report.Skipped++; return false; }
            if (roles.Count > 1)
            {
                report.Ambiguous++;
                report.Details.Add(path + " :: " + NodePath(node.transform) + " 多档匹配：" + string.Join(",", roles));
                return false;
            }

            node.AddComponent<UiTextStyleRef>().Role = roles[0];
            report.Added++;
            report.Direct++;
            return true;
        }

        private static bool MatchesTmp(TMP_Text tmp, UiTextStyleDef def)
            => Mathf.Abs(tmp.fontSize - def.FontSize) < 0.01f
               && tmp.alignment == def.TmpAlignment
               && tmp.enableWordWrapping == def.WordWrap
               && tmp.overflowMode == (def.Truncate ? TextOverflowModes.Truncate : TextOverflowModes.Overflow)
               && ((tmp.fontStyle & FontStyles.Bold) != 0) == def.Bold;

        private static bool MatchesLegacy(Text legacy, UiTextStyleDef def)
            => legacy.fontSize == (int)def.FontSize
               && legacy.alignment == def.LegacyAlignment
               && legacy.horizontalOverflow == (def.WordWrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow)
               && ((legacy.fontStyle & FontStyle.Bold) != 0) == def.Bold;

        // ---------------- 辅助 ----------------

        private static string NodePath(Transform t)
        {
            var s = t.name;
            for (var p = t.parent; p != null; p = p.parent) s = p.name + "/" + s;
            return s;
        }

        private static IEnumerable<string> PrefabPaths(IReadOnlyList<string> scanDirs)
        {
            var dirs = scanDirs ?? DefaultScanDirs;
            var paths = new List<string>();
            foreach (var dir in dirs)
            {
                if (!AssetDatabase.IsValidFolder(dir)) continue;
                foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { dir }))
                    paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            }
            paths.Sort(System.StringComparer.Ordinal);
            return paths;
        }
    }
}
