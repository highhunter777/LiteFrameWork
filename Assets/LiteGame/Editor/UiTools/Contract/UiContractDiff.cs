using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteGame.Editor.UiTools.Contracts
{
    public enum UiContractChangeKind
    {
        Added,
        Removed,
        Renamed,
        Changed,
        Visual,
        SourceOnly,
    }

    public sealed class UiContractChange
    {
        public UiContractChangeKind Kind;
        public string Section;
        public string Key;
        public string Detail;
        public bool Breaking;
    }

    public sealed class UiContractDiffResult
    {
        public readonly List<UiContractChange> Changes = new List<UiContractChange>();
        public bool HasBreaking => Changes.Exists(x => x.Breaking);
        public bool IsEmpty => Changes.Count == 0;
    }

    /// <summary>
    /// 契约 diff（《UI 编辑器工具链专项设计》§5.4）：契约 diff 即 UI API diff。
    /// 新增字段/控件/模板为兼容变更；删除、改名、类型变化、文案 key 变化为破坏性变更；
    /// 纯样式变化与 sourceHash-only 变化不判 API 破坏。
    /// </summary>
    public static class UiContractDiff
    {
        public static UiContractDiffResult Compare(UiPageContract baseline, UiPageContract current)
        {
            if (baseline == null) throw new ArgumentNullException(nameof(baseline));
            if (current == null) throw new ArgumentNullException(nameof(current));

            var result = new UiContractDiffResult();
            ComparePage(baseline.Page, current.Page, result);
            CompareControls(baseline.Controls, current.Controls, result);
            CompareText(baseline.Text, current.Text, result);
            CompareStyles(baseline.Styles, current.Styles, result);
            CompareTemplates(baseline.Templates, current.Templates, result);
            CompareEvents(baseline.Events, current.Events, result);

            if (result.IsEmpty && !string.Equals(baseline.SourceHash, current.SourceHash, StringComparison.Ordinal))
            {
                Add(result, UiContractChangeKind.SourceOnly, "sourceHash", baseline.SourceHash,
                    $"源指纹变化：{Short(baseline.SourceHash)} → {Short(current.SourceHash)}", false);
            }
            return result;
        }

        private static void ComparePage(UiContractPage a, UiContractPage b, UiContractDiffResult result)
        {
            if (a == null || b == null)
            {
                if (!ReferenceEquals(a, b)) Add(result, UiContractChangeKind.Changed, "page", null, "页面段缺失/新增", true);
                return;
            }

            // 页面段破坏性口径：只豁免“草稿晋升”（baseline=0 → 真实）；反向（真实→0，表行被删/回退）
            // 仍判破坏性。layer/fullScreen/cacheStrategy 视为运行时/维护元数据，变化只进 Changed。
            CheckPageField(a.FormId != b.FormId, "formId", a.FormId, b.FormId, a.FormId != 0, result);
            CheckPageField(!string.Equals(a.Name, b.Name, StringComparison.Ordinal), "name", a.Name, b.Name, true, result);
            CheckPageField(!string.Equals(a.PrefabPath, b.PrefabPath, StringComparison.Ordinal), "prefabPath", a.PrefabPath, b.PrefabPath, true, result);
            CheckPageField(!string.Equals(a.LuaPath, b.LuaPath, StringComparison.Ordinal), "luaPath", a.LuaPath, b.LuaPath,
                !(string.IsNullOrEmpty(a.LuaPath) && !string.IsNullOrEmpty(b.LuaPath)), result);
            CheckPageField(a.Layer != b.Layer, "layer", a.Layer, b.Layer, false, result);
            CheckPageField(a.FullScreen != b.FullScreen, "fullScreen", a.FullScreen, b.FullScreen, false, result);
            CheckPageField(!string.Equals(a.CacheStrategy, b.CacheStrategy, StringComparison.Ordinal), "cacheStrategy", a.CacheStrategy, b.CacheStrategy, false, result);
            CheckPageField(!string.Equals(a.Owner, b.Owner, StringComparison.Ordinal), "owner", a.Owner, b.Owner, false, result);
            CheckPageField(!string.Equals(a.DesignRef, b.DesignRef, StringComparison.Ordinal), "designRef", a.DesignRef, b.DesignRef, false, result);
        }

        private static void CheckPageField(bool changed, string field, object before, object after,
            bool breaking, UiContractDiffResult result)
        {
            if (!changed) return;
            Add(result, UiContractChangeKind.Changed, "page", field,
                $"{field}：{before ?? "null"} → {after ?? "null"}", breaking);
        }

        private static void CompareControls(List<UiContractControl> baseline, List<UiContractControl> current,
            UiContractDiffResult result)
        {
            var a = ToMap(baseline, x => x.BindName);
            var b = ToMap(current, x => x.BindName);
            var handledCurrent = new HashSet<string>(StringComparer.Ordinal);

            foreach (UiContractControl old in baseline ?? new List<UiContractControl>())
            {
                if (b.TryGetValue(old.BindName, out UiContractControl now))
                {
                    if (!string.Equals(old.Type, now.Type, StringComparison.Ordinal))
                        Add(result, UiContractChangeKind.Changed, "controls", now.BindName,
                            $"类型：{old.Type} → {now.Type}", true);
                    if (!string.Equals(old.Kind, now.Kind, StringComparison.Ordinal))
                        Add(result, UiContractChangeKind.Changed, "controls", now.BindName,
                            $"驱动方式：{old.Kind} → {now.Kind}", true);
                    if (old.Required != now.Required)
                        Add(result, UiContractChangeKind.Changed, "controls", now.BindName,
                            $"required：{old.Required} → {now.Required}", false);
                    if (!string.Equals(old.NodePath, now.NodePath, StringComparison.Ordinal))
                        Add(result, UiContractChangeKind.Changed, "controls", now.BindName,
                            $"节点路径：{old.NodePath} → {now.NodePath}", false);
                    continue;
                }

                UiContractControl renamed = current?.FirstOrDefault(x => string.Equals(x.NodePath, old.NodePath, StringComparison.Ordinal));
                if (renamed != null)
                {
                    handledCurrent.Add(renamed.BindName);
                    Add(result, UiContractChangeKind.Renamed, "controls", renamed.BindName,
                        $"{old.BindName} → {renamed.BindName}", true);
                }
                else
                {
                    Add(result, UiContractChangeKind.Removed, "controls", old.BindName, "控件已移除", true);
                }
            }

            foreach (UiContractControl now in current ?? new List<UiContractControl>())
            {
                if (a.ContainsKey(now.BindName) || handledCurrent.Contains(now.BindName)) continue;
                bool renamedFromOther = baseline != null && baseline.Any(
                    x => string.Equals(x.NodePath, now.NodePath, StringComparison.Ordinal) && !a.ContainsKey(now.BindName));
                if (renamedFromOther) continue;
                Add(result, UiContractChangeKind.Added, "controls", now.BindName, "新增控件", false);
            }
        }

        private static void CompareText(List<UiContractTextBinding> baseline, List<UiContractTextBinding> current,
            UiContractDiffResult result)
        {
            var a = ToMap(baseline, x => x.NodePath);
            var b = ToMap(current, x => x.NodePath);
            foreach (UiContractTextBinding old in baseline ?? new List<UiContractTextBinding>())
            {
                if (!b.TryGetValue(old.NodePath, out UiContractTextBinding now))
                {
                    Add(result, UiContractChangeKind.Removed, "text", old.NodePath, "静态文案已移除", true);
                    continue;
                }
                if (!string.Equals(old.Key, now.Key, StringComparison.Ordinal))
                    Add(result, UiContractChangeKind.Changed, "text", old.NodePath, $"key：{old.Key} → {now.Key}", true);
                if (old.Args != now.Args)
                    Add(result, UiContractChangeKind.Changed, "text", old.NodePath, $"args：{old.Args} → {now.Args}", true);
            }
            foreach (UiContractTextBinding now in current ?? new List<UiContractTextBinding>())
                if (!a.ContainsKey(now.NodePath))
                    Add(result, UiContractChangeKind.Added, "text", now.NodePath, "新增静态文案", false);
        }

        private static void CompareStyles(List<UiContractStyleBinding> baseline, List<UiContractStyleBinding> current,
            UiContractDiffResult result)
        {
            var a = ToMap(baseline, x => x.NodePath);
            var b = ToMap(current, x => x.NodePath);
            foreach (UiContractStyleBinding old in baseline ?? new List<UiContractStyleBinding>())
            {
                if (!b.TryGetValue(old.NodePath, out UiContractStyleBinding now))
                {
                    Add(result, UiContractChangeKind.Visual, "styles", old.NodePath, "样式声明已移除", false);
                    continue;
                }
                if (!string.Equals(old.Role, now.Role, StringComparison.Ordinal)
                    || !string.Equals(old.Token, now.Token, StringComparison.Ordinal))
                    Add(result, UiContractChangeKind.Visual, "styles", old.NodePath,
                        $"样式：{old.Role}/{old.Token} → {now.Role}/{now.Token}", false);
            }
            foreach (UiContractStyleBinding now in current ?? new List<UiContractStyleBinding>())
                if (!a.ContainsKey(now.NodePath))
                    Add(result, UiContractChangeKind.Visual, "styles", now.NodePath, "新增样式声明", false);
        }

        private static void CompareTemplates(List<UiContractTemplateUse> baseline, List<UiContractTemplateUse> current,
            UiContractDiffResult result)
        {
            var a = ToMap(baseline, x => x.NodePath);
            var b = ToMap(current, x => x.NodePath);
            foreach (UiContractTemplateUse old in baseline ?? new List<UiContractTemplateUse>())
            {
                if (!b.TryGetValue(old.NodePath, out UiContractTemplateUse now))
                {
                    Add(result, UiContractChangeKind.Removed, "templates", old.NodePath, "模板依赖已移除", true);
                    continue;
                }
                if (!string.Equals(old.Template, now.Template, StringComparison.Ordinal))
                    Add(result, UiContractChangeKind.Changed, "templates", old.NodePath,
                        $"模板：{old.Template} → {now.Template}", true);
                if (!string.Equals(old.BindingHash, now.BindingHash, StringComparison.Ordinal))
                    Add(result, UiContractChangeKind.Changed, "templates", old.NodePath,
                        "模板绑定面 hash 变化", false);
            }
            foreach (UiContractTemplateUse now in current ?? new List<UiContractTemplateUse>())
                if (!a.ContainsKey(now.NodePath))
                    Add(result, UiContractChangeKind.Added, "templates", now.NodePath, "新增模板依赖", false);
        }

        private static void CompareEvents(List<UiContractEventBinding> baseline, List<UiContractEventBinding> current,
            UiContractDiffResult result)
        {
            var a = ToMap(baseline, x => x.BindName + "|" + x.Event);
            var b = ToMap(current, x => x.BindName + "|" + x.Event);
            foreach (string key in a.Keys)
                if (!b.ContainsKey(key))
                    Add(result, UiContractChangeKind.Removed, "events", key, "事件声明已移除", false);
            foreach (string key in b.Keys)
                if (!a.ContainsKey(key))
                    Add(result, UiContractChangeKind.Added, "events", key, "新增事件声明", false);
        }

        private static Dictionary<string, T> ToMap<T>(List<T> items, Func<T, string> keySelector)
        {
            var map = new Dictionary<string, T>(StringComparer.Ordinal);
            if (items == null) return map;
            foreach (T item in items)
            {
                string key = keySelector(item);
                if (!string.IsNullOrEmpty(key)) map[key] = item;
            }
            return map;
        }

        private static void Add(UiContractDiffResult result, UiContractChangeKind kind, string section,
            string key, string detail, bool breaking)
        {
            result.Changes.Add(new UiContractChange
            {
                Kind = kind,
                Section = section,
                Key = key,
                Detail = detail,
                Breaking = breaking,
            });
        }

        private static string Short(string hash)
            => string.IsNullOrEmpty(hash) ? "-" : hash.Substring(0, Math.Min(19, hash.Length));
    }
}
