using System.Collections.Generic;
using LiteGame.Editor.UiTools.Binding;
using LiteGame.Editor.UiTools.Naming;
using LiteGame.Editor.UiTools.Validation;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Editor.UiTools.Compose
{
    /// <summary>控件库条目（模板 prefab 的登记面）。</summary>
    public sealed class UiWidgetEntry
    {
        public GameObject Asset;
        public string Path;
        public string TemplateName;    // 模板本体名（Tmp_<控件类型>）
        public string UsageSegment;    // 业务使用段（去 Tmp_）；结构工具件不占类型段 → 空
    }

    /// <summary>
    /// 控件库清单（《UI编辑器工具专项设计》§5"控件库"）：**目录即清单**——扫
    /// <see cref="UiValidator.WidgetsDir"/> 下 `Tmp_` 前缀的模板 prefab，不另造清单文件
    /// （《UI命名词表与控件清单》§1.3 是内容源，模板落盘即登记）。
    /// 纯逻辑面（零 Sirenix）。
    /// </summary>
    public static class UiWidgetLibrary
    {
        /// <summary>载入全部控件模板条目（按模板名稳定序）。</summary>
        public static List<UiWidgetEntry> Load()
        {
            var entries = new List<UiWidgetEntry>();
            if (!AssetDatabase.IsValidFolder(UiValidator.WidgetsDir)) return entries;

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { UiValidator.WidgetsDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!IsTemplateName(System.IO.Path.GetFileNameWithoutExtension(path))) continue;
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null) continue;
                entries.Add(new UiWidgetEntry
                {
                    Asset = asset,
                    Path = path,
                    TemplateName = asset.name,
                    UsageSegment = UsageSegmentOf(asset.name),
                });
            }

            entries.Sort((a, b) => string.CompareOrdinal(a.TemplateName, b.TemplateName));
            return entries;
        }

        /// <summary>模板本体名判据：词表模板前缀（`Tmp_`）起头且过 §9.2 文法。</summary>
        public static bool IsTemplateName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            bool hasTemplatePrefix = false;
            foreach (var prefix in UiNamingWords.TemplatePrefixes)
                if (name.StartsWith(prefix, System.StringComparison.Ordinal)) { hasTemplatePrefix = true; break; }
            return hasTemplatePrefix && UiBindingRename.MatchesGrammar(name);
        }

        /// <summary>业务使用段（去 `Tmp_` 后加下划线）；结构工具件（SafeArea/EventRelay/Baseline）不占类型段 → 空。</summary>
        public static string UsageSegmentOf(string templateName)
        {
            if (!UiNamingWords.TryParse(templateName, out string _, out string typeName)) return null;
            string segment = typeName + "_";
            foreach (var known in UiNamingWords.TemplateSegments)
                if (known == segment) return segment;
            return null;
        }
    }
}
