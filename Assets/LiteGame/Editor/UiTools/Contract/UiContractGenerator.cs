using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using LiteClient.UI;
using LiteCodeGen;
using LiteGame.Editor.UiTools.Binding;
using LiteGame.Editor.UiTools.Page;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.Editor.UiTools.Contracts
{
    public sealed class UiContractGenerationResult
    {
        public UiPageContract Contract;
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        public bool Ok => Errors.Count == 0;
        public string ErrorText => string.Join("\n", Errors);
    }

    /// <summary>
    /// 页面契约生成器（《UI 编辑器工具链专项设计》§5）：prefab + tbuiform 行 → 只读契约 JSON。
    ///
    /// 真相与过滤规则：
    /// - 只收非空 `BindName`；页面内唯一、类型可解析；
    /// - `Clone_` 子树、嵌套 `BindRoot`、模板内部件不进 `controls[]`；
    /// - 嵌套 `Tmp_*` 实例只记一条 `templates[]`，不展开内部；
    /// - 无时间戳、深度优先稳定排序、重复生成零 diff。
    /// </summary>
    public static class UiContractGenerator
    {
        /// <summary>草稿契约的层/全屏默认（单源）：登记后重生成覆盖为真实身份。</summary>
        public const int DraftLayer = 0;

        /// <summary>草稿契约默认全屏（单源）。</summary>
        public const bool DraftFullScreen = true;

        private static readonly Regex PageNamePattern =
            new Regex("^(Page|Hud|Tmp)_[A-Za-z0-9]+$", RegexOptions.CultureInvariant);

        private static readonly Regex PrefabPathPattern =
            new Regex("^Assets/ShareResource/UI/(Screens|Hud)/.+\\.prefab$", RegexOptions.CultureInvariant);

        private static readonly HashSet<string> CacheStrategies =
            new HashSet<string>(Enum.GetNames(typeof(UICacheStrategy)), StringComparer.Ordinal);

        public static UiContractGenerationResult Generate(string prefabPath, UiPageMetadata metadata)
        {
            var result = new UiContractGenerationResult();
            string path = Normalize(prefabPath);

            if (metadata == null)
            {
                result.Errors.Add("页面元数据为空——契约生成需要 tbuiform 行（formId/luaPath/layer/fullScreen）");
                return result;
            }
            if (metadata.IsDraft)
            {
                if (metadata.FormId != 0) result.Errors.Add("草稿契约的 formId 必须为 0");
            }
            else if (metadata.FormId <= 0)
            {
                result.Errors.Add("formId 必须为正整数");
            }
            if (metadata.Layer < 0 || metadata.Layer > 3) result.Errors.Add("layer 必须在 0..3");
            if (!string.IsNullOrEmpty(metadata.CacheStrategy) && !CacheStrategies.Contains(metadata.CacheStrategy))
                result.Errors.Add("cacheStrategy 非法：" + metadata.CacheStrategy);

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                result.Errors.Add("prefab 不存在：" + path);
                return result;
            }

            string fileName = Path.GetFileNameWithoutExtension(path);
            if (!string.Equals(prefab.name, fileName, StringComparison.Ordinal))
                result.Errors.Add($"prefab 根名({prefab.name})与文件名({fileName})不一致");
            if (!IsContractPageName(prefab.name))
                result.Errors.Add($"页面根名不符合 Page_/Hud_/Tmp_ 文法：{prefab.name}");
            if (!PrefabPathPattern.IsMatch(path))
                result.Errors.Add("页面 prefab 必须位于 Assets/ShareResource/UI/Screens 或 Hud：" + path);

            string guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid)) result.Errors.Add("取不到 prefab GUID：" + path);
            if (!result.Ok) return result;

            var contract = new UiPageContract
            {
                Generator = new UiContractGeneratorInfo { Name = "ui-contract", Version = "1.0" },
                Page = new UiContractPage
                {
                    FormId = metadata.FormId,
                    Name = prefab.name,
                    PrefabPath = path,
                    PrefabGuid = guid,
                    LuaPath = string.IsNullOrEmpty(metadata.LuaPath) ? null : metadata.LuaPath,
                    Layer = metadata.Layer,
                    FullScreen = metadata.FullScreen,
                    CacheStrategy = string.IsNullOrEmpty(metadata.CacheStrategy)
                        ? UiPageContract.DefaultCacheStrategy
                        : metadata.CacheStrategy,
                    Owner = string.IsNullOrEmpty(metadata.Owner) ? null : metadata.Owner,
                    DesignRef = string.IsNullOrEmpty(metadata.DesignRef) ? null : metadata.DesignRef,
                },
            };

            GameObject contents = null;
            try
            {
                contents = PrefabUtility.LoadPrefabContents(path);
                Collect(contents, contract, result);
            }
            catch (Exception ex)
            {
                result.Errors.Add("读取 prefab 内容失败：" + ex.Message);
            }
            finally
            {
                if (contents != null) PrefabUtility.UnloadPrefabContents(contents);
            }

            if (!result.Ok) return result;

            contract.Controls.Sort((a, b) => string.CompareOrdinal(a.NodePath, b.NodePath));
            contract.Text.Sort((a, b) => string.CompareOrdinal(a.NodePath, b.NodePath));
            contract.Styles.Sort((a, b) => string.CompareOrdinal(a.NodePath, b.NodePath));
            contract.Templates.Sort((a, b) => string.CompareOrdinal(a.NodePath, b.NodePath));
            contract.Events.Sort((a, b) => string.CompareOrdinal(a.BindName + "|" + a.Event, b.BindName + "|" + b.Event));
            contract.SourceHash = ComputeSourceHash(contract);
            result.Contract = contract;
            return result;
        }

        /// <summary>按 tbuiform 行生成（页面登记后使用）。读取失败/未登记显式报错，不伪造 metadata。</summary>
        public static UiContractGenerationResult GenerateRegistered(string prefabPath)
        {
            return GenerateRegistered(prefabPath, UiPageTableHelper.ListPages());
        }

        /// <summary>
        /// 按已读页面行生成（批处理用：表只读一次）。同一 prefab 多行登记时取**首行**，
        /// 其余行由页面清单 `forms[]` 承担，并在 <see cref="UiContractGenerationResult.Warnings"/> 显式提示。
        /// </summary>
        public static UiContractGenerationResult GenerateRegistered(string prefabPath,
            IReadOnlyList<UiPageTableRow> rows)
        {
            string normalized = Normalize(prefabPath);
            List<UiPageTableRow> matches = (rows ?? Array.Empty<UiPageTableRow>())
                .Where(row => row != null && string.Equals(Normalize(row.PrefabPath), normalized, StringComparison.Ordinal))
                .ToList();
            if (matches.Count == 0)
            {
                var missing = new UiContractGenerationResult();
                missing.Errors.Add("tbuiform 未登记页面：" + normalized + "（或页面清单读取失败）");
                return missing;
            }

            UiPageTableRow primary = matches[0];
            UiContractGenerationResult result = Generate(normalized, new UiPageMetadata
            {
                FormId = primary.id,
                LuaPath = string.IsNullOrEmpty(primary.lua) ? null : primary.lua,
                Layer = primary.layer,
                FullScreen = primary.fullScreen,
                CacheStrategy = UiPageContract.DefaultCacheStrategy,
            });

            if (matches.Count > 1)
            {
                result.Warnings.Add("多行登记：" + string.Join("/", matches.Select(row => row.id))
                    + "，契约取首行 " + primary.id + "；其余行见页面清单 forms[]");
            }
            return result;
        }

        /// <summary>页面根名是否属于契约面（<c>Page_/Hud_/Tmp_</c>；非常驻壳不产契约）。</summary>
        public static bool IsContractPageName(string pageName)
        {
            return !string.IsNullOrEmpty(pageName) && PageNamePattern.IsMatch(pageName);
        }

        /// <summary>生成并写盘；负例只返回错误，不落任何文件。</summary>
        public static UiContractGenerationResult GenerateToDisk(string prefabPath, UiPageMetadata metadata, string outputPath = null)
        {
            UiContractGenerationResult result = Generate(prefabPath, metadata);
            if (!result.Ok) return result;

            string path = string.IsNullOrEmpty(outputPath)
                ? UiContractPaths.ForPage(result.Contract.Page.Name)
                : outputPath.Replace('\\', '/');
            if (!path.StartsWith("Assets/", StringComparison.Ordinal))
            {
                result.Errors.Add("契约输出路径必须在 Assets/ 下：" + path);
                result.Contract = null;
                return result;
            }
            try
            {
                UiContractStore.Save(path, result.Contract);
            }
            catch (Exception ex)
            {
                result.Errors.Add("契约写盘失败：" + ex.Message);
                result.Contract = null;
            }
            return result;
        }

        /// <summary>
        /// 把页面清单维护字段（Owner/DesignRef/CacheStrategy）投影进契约页面元数据（§5.2 字段模型——清单是维护字段的家；
        /// 元数据不入 sourceHash，故投影后不重算指纹）。清单缺条目（null）＝不臆造，透传 null 由调用方告警。
        /// </summary>
        public static void ApplyManifestEditorial(UiPageContract contract, Page.UiPageManifestEntry entry)
        {
            if (contract == null) return;
            contract.Page.Owner = string.IsNullOrEmpty(entry?.Owner) ? null : entry.Owner;
            contract.Page.DesignRef = string.IsNullOrEmpty(entry?.DesignRef) ? null : entry.DesignRef;
            if (!string.IsNullOrEmpty(entry?.CacheStrategy) && UiPageContract.IsValidCacheStrategy(entry.CacheStrategy))
                contract.Page.CacheStrategy = entry.CacheStrategy;
        }

        /// <summary>
        /// 应用 Lua 事件接线（§5.2「events[] 可选——契约只做对账提示」）：按 <c>bindName|event</c> 稳定排序写入
        /// <see cref="UiPageContract.Events"/> 并重算 <see cref="UiPageContract.SourceHash"/>（event 行在指纹内，
        /// §5.2.1）。同一集合重复应用结果一致（幂等）。
        /// </summary>
        public static void ApplyEvents(UiPageContract contract, IReadOnlyList<Reference.UiLuaEventWiring> wiring)
        {
            if (contract == null) return;
            contract.Events.Clear();
            foreach (Reference.UiLuaEventWiring wire in wiring ?? Array.Empty<Reference.UiLuaEventWiring>())
            {
                if (wire == null || string.IsNullOrEmpty(wire.Name) || string.IsNullOrEmpty(wire.Event)) continue;
                contract.Events.Add(new UiContractEventBinding
                {
                    BindName = wire.Name,
                    Event = wire.Event,
                    Handler = string.IsNullOrEmpty(wire.Handler) ? null : wire.Handler,
                });
            }
            contract.Events.Sort((a, b) => string.CompareOrdinal(a.BindName + "|" + a.Event, b.BindName + "|" + b.Event));
            contract.SourceHash = ComputeSourceHash(contract);
        }

        private static void Collect(GameObject root, UiPageContract contract, UiContractGenerationResult result)
        {
            var seenBindNames = new HashSet<string>(StringComparer.Ordinal);
            var templateHashes = new Dictionary<string, string>(StringComparer.Ordinal);

            // 一次遍历（遍历单源 UiBindSurfaceWalker）：Clone_ 子树、嵌套 BindRoot 子树、模板内部件
            // 都不属于本页接口面；模板实例根只记一条 templates[]，不展开内部。
            foreach (UiSurfaceNode node in UiBindSurfaceWalker.Walk(root))
            {
                if (node.InCloneScope || node.InsideNestedBindRoot || node.InsideTemplate) continue;

                CollectControl(node, contract, seenBindNames, result);
                CollectText(node, contract, result);
                CollectStyle(node, contract, result);
                if (!string.IsNullOrEmpty(node.TemplateAssetPath))
                    CollectTemplate(node, contract, templateHashes, result);
            }
        }

        private static void CollectControl(UiSurfaceNode node, UiPageContract contract,
            HashSet<string> seenBindNames, UiContractGenerationResult result)
        {
            if (node.Bind == null) return;
            string bindName = (node.Bind.BindName ?? string.Empty).Trim();
            if (bindName.Length == 0) return;

            if (!seenBindNames.Add(bindName))
            {
                result.Errors.Add($"重复 BindName：{bindName}（{node.Path}）");
                return;
            }

            string type = ResolveType(node.Bind, node.Path, result);
            if (type == null) return;

            bool command = IsCommandTarget(node.Bind.ResolveTarget());
            contract.Controls.Add(new UiContractControl
            {
                BindName = bindName,
                NodePath = node.Path,
                Type = type,
                Kind = command ? "Command" : "None",
                Required = command,
            });
        }

        private static void CollectText(UiSurfaceNode node, UiPageContract contract,
            UiContractGenerationResult result)
        {
            LTextKeyRef textRef = node.Node.GetComponent<LTextKeyRef>();
            if (textRef == null) return;

            if (string.IsNullOrWhiteSpace(textRef.Key))
            {
                result.Errors.Add($"LTextKeyRef.Key 为空（{node.Path}）");
                return;
            }
            if (node.Node.GetComponent<TMPro.TMP_Text>() == null
                && node.Node.GetComponent<UnityEngine.UI.Text>() == null)
            {
                result.Errors.Add($"LTextKeyRef 节点缺少 TMP_Text/Text（{node.Path}）");
                return;
            }

            contract.Text.Add(new UiContractTextBinding
            {
                NodePath = node.Path,
                Key = textRef.Key,
                Args = 0,
            });
        }

        private static void CollectStyle(UiSurfaceNode node, UiPageContract contract,
            UiContractGenerationResult result)
        {
            UiTextStyleRef styleRef = node.Node.GetComponent<UiTextStyleRef>();
            if (styleRef == null) return;

            string role = MapRole(styleRef.Role);
            if (role == null)
            {
                result.Errors.Add($"UiTextStyleRef 角色无法映射（{node.Path}）");
                return;
            }

            contract.Styles.Add(new UiContractStyleBinding
            {
                NodePath = node.Path,
                Role = role,
            });
        }

        private static void CollectTemplate(UiSurfaceNode node, UiPageContract contract,
            Dictionary<string, string> templateHashes, UiContractGenerationResult result)
        {
            if (!(node.TemplateName ?? string.Empty).StartsWith("Tmp_", StringComparison.Ordinal))
            {
                result.Warnings.Add($"嵌套 prefab 不是 Tmp_ 模板，作为不透明节点跳过：{node.TemplateAssetPath}");
                return;
            }

            if (!templateHashes.TryGetValue(node.TemplateAssetPath, out string hash))
            {
                GameObject template = AssetDatabase.LoadAssetAtPath<GameObject>(node.TemplateAssetPath);
                if (template == null)
                {
                    hash = null;
                    result.Warnings.Add("模板 prefab 不存在，bindingHash 省略：" + node.TemplateAssetPath);
                }
                else if (template.GetComponent<BindRoot>() == null
                         && template.GetComponentInChildren<BindNode>(true) == null)
                {
                    hash = null;   // 无绑定面的模板不写常量空 hash
                }
                else
                {
                    hash = UiBindGenerationHash.ComputeHash(template);
                }
                templateHashes[node.TemplateAssetPath] = hash;
            }

            contract.Templates.Add(new UiContractTemplateUse
            {
                NodePath = node.Path,
                Template = node.TemplateName,
                BindingHash = hash,
            });
        }

        private static string ResolveType(BindNode bind, string nodePath, UiContractGenerationResult result)
        {
            string custom = (bind.CustomTypeName ?? string.Empty).Trim();
            if (custom == "GameObject" || custom == "UnityEngine.GameObject")
                return typeof(GameObject).FullName;

            Component target = bind.ResolveTarget();
            if (target != null) return target.GetType().FullName;

            result.Errors.Add($"BindName {bind.BindName} 类型不可解析（{nodePath}，CustomTypeName={custom}）");
            return null;
        }

        private static bool IsCommandTarget(Component target)
        {
            if (target == null) return false;
            return target is Selectable
                   || target is ScrollRect
                   || target is VirtualList
                   || target is SimpleListView
                   || target is UIWidget
                   || target is ProgressBar
                   || target is Stepper
                   || target is UIDropdownWrap;
        }

        private static string MapRole(UiTextRole role)
        {
            switch (role)
            {
                case UiTextRole.Title: return "Title";
                case UiTextRole.Body: return "Body";
                case UiTextRole.Caption: return "Caption";
                case UiTextRole.LabelSingle: return "Label";
                case UiTextRole.Emphasis: return "Emphasis";
                default: return null;
            }
        }

        private static string ComputeSourceHash(UiPageContract contract)
        {
            var sb = new StringBuilder();
            sb.Append("ui-page-contract/").Append(UiPageContract.CurrentSchemaVersion).Append('\n');

            foreach (UiContractControl c in contract.Controls)
            {
                sb.Append("control|").Append(c.BindName).Append('|').Append(c.NodePath).Append('|')
                    .Append(c.Type).Append('|').Append(c.Kind).Append('|').Append(c.Required ? '1' : '0').Append('\n');
            }
            foreach (UiContractTextBinding t in contract.Text)
            {
                sb.Append("text|").Append(t.NodePath).Append('|').Append(t.Key).Append('|').Append(t.Args).Append('\n');
            }
            foreach (UiContractStyleBinding s in contract.Styles)
            {
                sb.Append("style|").Append(s.NodePath).Append('|').Append(s.Role).Append('|')
                    .Append(s.Token ?? string.Empty).Append('\n');
            }
            foreach (UiContractTemplateUse t in contract.Templates)
            {
                sb.Append("template|").Append(t.NodePath).Append('|').Append(t.Template).Append('|')
                    .Append(t.BindingHash ?? string.Empty).Append('\n');
            }
            foreach (UiContractEventBinding e in contract.Events)
            {
                sb.Append("event|").Append(e.BindName).Append('|').Append(e.Event).Append('|')
                    .Append(e.Handler ?? string.Empty).Append('\n');
            }

            using (var sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                return "sha256:" + BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static string Normalize(string prefabPath)
            => (prefabPath ?? string.Empty).Replace('\\', '/').Trim();
    }
}
