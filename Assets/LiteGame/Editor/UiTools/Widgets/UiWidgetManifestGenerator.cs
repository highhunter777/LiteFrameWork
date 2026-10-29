using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using LiteFramework.UI;
using LiteGame.Editor.UiTools.Binding;
using LiteGame.Editor.UiTools.Compose;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Editor.UiTools.Widgets
{
    /// <summary>清单生成结果：成功时带 <see cref="Manifest"/>；失败时 <see cref="Errors"/> 逐条可读、不写盘。</summary>
    public sealed class UiWidgetManifestGenerationResult
    {
        public UiWidgetManifest Manifest;
        public readonly List<string> Errors = new List<string>();

        public bool Ok => Errors.Count == 0;
        public string ErrorText => string.Join("\n", Errors);

        /// <summary>非阻断告警（单一来源＝<see cref="UiWidgetManifest.Warnings"/>，避免两份清单）。</summary>
        public List<string> Warnings => Manifest != null ? Manifest.Warnings : null;
    }

    /// <summary>
    /// 控件清单生成器（《UI 编辑器工具链专项设计》§8.3/§8.4）：`Tmp_*` 模板 + <see cref="WidgetSpec"/>
    /// + 受控属性白名单 + 绑定 hash → 确定性 <see cref="UiWidgetManifest"/>。
    ///
    /// 真相与校验口径：
    /// - 模板清单＝目录（<see cref="UiWidgetLibrary"/>，不另存清单）；
    /// - 声明＝模板根 `WidgetSpec`；属性＝控件字段上的 `UiWidgetProperty`（不重复声明）；
    /// - 缺 `WidgetSpec` 只告警（`specPresent=false`；Error 归 P1-4 校验规则）；
    /// - 插槽/事件/约束声明不一致＝Error（负例不写盘）；
    /// - 无时间戳、稳定排序、重复生成逐字节一致。
    /// </summary>
    public static class UiWidgetManifestGenerator
    {
        /// <summary>扫真实模板目录（`Assets/ShareResource/UI/Widgets`）生成清单。</summary>
        public static UiWidgetManifestGenerationResult Generate() => Generate((IEnumerable<string>)null);

        /// <summary>按显式 prefab 路径生成（测试/工具用；null＝扫模板目录）。</summary>
        public static UiWidgetManifestGenerationResult Generate(IEnumerable<string> prefabPaths)
        {
            var result = new UiWidgetManifestGenerationResult { Manifest = new UiWidgetManifest() };

            List<string> paths = prefabPaths == null
                ? UiWidgetLibrary.Load().Select(entry => entry.Path).ToList()
                : prefabPaths.Select(Normalize).Where(path => path.Length > 0).ToList();
            paths = paths.Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal).ToList();

            foreach (string path in paths)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    result.Errors.Add("模板 prefab 不存在：" + path);
                    continue;
                }
                result.Manifest.Widgets.Add(BuildEntry(prefab, path, result));
            }

            result.Manifest.Widgets.Sort((a, b) => string.CompareOrdinal(a.Template, b.Template));
            result.Manifest.Warnings.Sort(StringComparer.Ordinal);
            result.Manifest.SourceHash = ComputeSourceHash(result.Manifest);
            return result;
        }

        /// <summary>生成并写盘；负例只返回错误、不落任何文件。</summary>
        public static UiWidgetManifestGenerationResult GenerateToDisk(string outputPath = null)
        {
            UiWidgetManifestGenerationResult result = Generate();
            if (!result.Ok) return result;

            string path = string.IsNullOrEmpty(outputPath)
                ? UiWidgetManifestPaths.DefaultPath
                : outputPath.Replace('\\', '/');
            if (!path.StartsWith("Assets/", StringComparison.Ordinal))
            {
                result.Errors.Add("控件清单输出路径必须在 Assets/ 下：" + path);
                result.Manifest = null;
                return result;
            }
            try
            {
                UiWidgetManifestStore.Save(result.Manifest, path);
            }
            catch (Exception ex)
            {
                result.Errors.Add("控件清单写盘失败：" + ex.Message);
                result.Manifest = null;
            }
            return result;
        }

        // ---------------- 单模板投影 ----------------

        private static UiWidgetManifestEntry BuildEntry(GameObject prefab, string path,
            UiWidgetManifestGenerationResult result)
        {
            string fileName = Path.GetFileNameWithoutExtension(path);
            if (!string.Equals(prefab.name, fileName, StringComparison.Ordinal))
                result.Errors.Add($"模板根名({prefab.name})与文件名({fileName})不一致");
            if (!UiWidgetLibrary.IsTemplateName(prefab.name))
                result.Errors.Add("模板名不合 Tmp_ 文法：" + prefab.name);

            var entry = new UiWidgetManifestEntry
            {
                Template = prefab.name,
                PrefabPath = path,
                PrefabGuid = AssetDatabase.AssetPathToGUID(path),
                UsageSegment = UiWidgetLibrary.UsageSegmentOf(prefab.name),
                SpecPresent = prefab.GetComponent<WidgetSpec>() != null,
                BindingHash = UiBindGenerationHash.ComputeHash(prefab),
            };

            var spec = prefab.GetComponent<WidgetSpec>();
            if (spec == null)
            {
                result.Manifest.Warnings.Add($"{prefab.name}：缺 WidgetSpec（模板根未声明能力；按 UI 制作规范 §3.2 应声明）");
            }
            else
            {
                CollectSpec(prefab, spec, entry, result);
            }

            CollectProperties(prefab, entry);
            return entry;
        }

        private static void CollectSpec(GameObject prefab, WidgetSpec spec, UiWidgetManifestEntry entry,
            UiWidgetManifestGenerationResult result)
        {
            entry.Owner = Trimmed(spec.Owner);
            entry.Summary = Trimmed(spec.Summary);
            if (entry.Owner == null) result.Manifest.Warnings.Add($"{prefab.name}：WidgetSpec.Owner 为空");
            if (entry.Summary == null) result.Manifest.Warnings.Add($"{prefab.name}：WidgetSpec.Summary 为空");

            CollectSlots(prefab, spec, entry, result);
            CollectStates(prefab, spec, entry, result);
            CollectEvents(prefab, spec, entry, result);
            entry.SizeBehavior = spec.SizeBehavior.ToString();
            CollectConstraints(prefab, spec, entry, result);
        }

        private static void CollectSlots(GameObject prefab, WidgetSpec spec, UiWidgetManifestEntry entry,
            UiWidgetManifestGenerationResult result)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (WidgetSlotSpec slot in spec.Slots ?? Array.Empty<WidgetSlotSpec>())
            {
                if (slot == null) continue;
                string name = (slot.Name ?? string.Empty).Trim();
                if (name.Length == 0)
                {
                    result.Errors.Add($"{prefab.name}：插槽名为空");
                    continue;
                }
                if (!WidgetSpecAuthoring.IsSlotName(name))
                {
                    result.Errors.Add($"{prefab.name}：插槽 {name} 命名不合约定（Cot_*/Slot_*）");
                    continue;
                }
                if (!seen.Add(name))
                {
                    result.Errors.Add($"{prefab.name}：重复插槽 {name}");
                    continue;
                }
                if (prefab.transform.Find(name) == null)
                {
                    result.Errors.Add($"{prefab.name}：插槽节点不存在 {name}");
                    continue;
                }
                entry.Slots.Add(new UiWidgetSlotEntry
                {
                    Name = name,
                    Required = slot.Required,
                    Description = Trimmed(slot.Description),
                });
            }
        }

        private static void CollectStates(GameObject prefab, WidgetSpec spec, UiWidgetManifestEntry entry,
            UiWidgetManifestGenerationResult result)
        {
            var seen = new HashSet<UiWidgetState>();
            var states = new List<UiWidgetState>();
            foreach (UiWidgetState state in spec.States ?? Array.Empty<UiWidgetState>())
            {
                if (!seen.Add(state))
                {
                    result.Errors.Add($"{prefab.name}：重复状态 {state}");
                    continue;
                }
                states.Add(state);
            }
            states.Sort((a, b) => ((int)a).CompareTo((int)b));
            foreach (UiWidgetState state in states) entry.States.Add(state.ToString());
        }

        private static void CollectEvents(GameObject prefab, WidgetSpec spec, UiWidgetManifestEntry entry,
            UiWidgetManifestGenerationResult result)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var events = new List<string>();
            foreach (string raw in spec.Events ?? Array.Empty<string>())
            {
                string declaration = (raw ?? string.Empty).Trim();
                if (declaration.Length == 0)
                {
                    result.Errors.Add($"{prefab.name}：事件名为空");
                    continue;
                }
                if (!seen.Add(declaration))
                {
                    result.Errors.Add($"{prefab.name}：重复事件 {declaration}");
                    continue;
                }
                if (!TryResolveEvent(prefab, declaration, out string reason))
                {
                    result.Errors.Add($"{prefab.name}：事件 {declaration} {reason}");
                    continue;
                }
                events.Add(declaration);
            }
            events.Sort(StringComparer.Ordinal);
            entry.Events = events;
        }

        private static bool TryResolveEvent(GameObject prefab, string declaration, out string reason)
        {
            reason = null;
            int dot = declaration.LastIndexOf('.');
            if (dot <= 0 || dot == declaration.Length - 1)
            {
                reason = "格式应为 类型.成员";
                return false;
            }

            string typeName = declaration.Substring(0, dot);
            string member = declaration.Substring(dot + 1);
            Type type = null;
            foreach (Component component in prefab.GetComponentsInChildren<Component>(true))
            {
                if (component == null) continue;
                if (!string.Equals(component.GetType().Name, typeName, StringComparison.Ordinal)) continue;
                type = component.GetType();
                break;
            }
            if (type == null)
            {
                reason = $"找不到组件类型 {typeName}";
                return false;
            }
            if (type.GetEvent(member, BindingFlags.Public | BindingFlags.Instance) != null) return true;
            if (type.GetField(member, BindingFlags.Public | BindingFlags.Instance) != null) return true;
            reason = $"成员 {member} 不是 {typeName} 的公开事件/字段";
            return false;
        }

        private static void CollectConstraints(GameObject prefab, WidgetSpec spec, UiWidgetManifestEntry entry,
            UiWidgetManifestGenerationResult result)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var constraints = new List<string>();
            foreach (string raw in spec.Constraints ?? Array.Empty<string>())
            {
                string constraint = (raw ?? string.Empty).Trim();
                if (constraint.Length == 0)
                {
                    result.Errors.Add($"{prefab.name}：约束为空串");
                    continue;
                }
                if (!seen.Add(constraint))
                {
                    result.Errors.Add($"{prefab.name}：重复约束 {constraint}");
                    continue;
                }
                constraints.Add(constraint);
            }
            constraints.Sort(StringComparer.Ordinal);
            entry.Constraints = constraints;
        }

        private static void CollectProperties(GameObject prefab, UiWidgetManifestEntry entry)
        {
            foreach (UiWidgetPropRow row in UiWidgetPropertyCatalog.Collect(prefab))
            {
                entry.Properties.Add(new UiWidgetPropertyEntry
                {
                    NodePath = row.NodePath,
                    Component = row.ComponentTypeName,
                    Member = row.MemberName,
                    Kind = row.Kind.ToString(),
                    DisplayName = Trimmed(row.DisplayName),
                    Tooltip = Trimmed(row.Tooltip),
                });
            }
        }

        // ---------------- 指纹 ----------------

        private static string ComputeSourceHash(UiWidgetManifest manifest)
        {
            var sb = new StringBuilder();
            sb.Append("ui-widget-manifest/").Append(UiWidgetManifest.CurrentSchemaVersion).Append('\n');

            foreach (UiWidgetManifestEntry widget in manifest.Widgets)
            {
                sb.Append("widget|").Append(widget.Template).Append('|')
                    .Append(widget.Owner ?? string.Empty).Append('|')
                    .Append(widget.Summary ?? string.Empty).Append('|')
                    .Append(widget.SizeBehavior ?? string.Empty).Append('\n');
                foreach (UiWidgetSlotEntry slot in widget.Slots)
                    sb.Append("slot|").Append(widget.Template).Append('|').Append(slot.Name).Append('|')
                        .Append(slot.Required ? '1' : '0').Append('|').Append(slot.Description ?? string.Empty).Append('\n');
                foreach (string state in widget.States)
                    sb.Append("state|").Append(widget.Template).Append('|').Append(state).Append('\n');
                foreach (string evt in widget.Events)
                    sb.Append("event|").Append(widget.Template).Append('|').Append(evt).Append('\n');
                foreach (string constraint in widget.Constraints)
                    sb.Append("constraint|").Append(widget.Template).Append('|').Append(constraint).Append('\n');
                foreach (UiWidgetPropertyEntry property in widget.Properties)
                    sb.Append("property|").Append(widget.Template).Append('|').Append(property.NodePath).Append('|')
                        .Append(property.Component).Append('|').Append(property.Member).Append('|')
                        .Append(property.Kind).Append('\n');
                sb.Append("binding|").Append(widget.Template).Append('|')
                    .Append(widget.BindingHash ?? string.Empty).Append('\n');
            }

            using (var sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                return "sha256:" + BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static string Trimmed(string value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static string Normalize(string path)
            => (path ?? string.Empty).Replace('\\', '/').Trim();
    }
}
