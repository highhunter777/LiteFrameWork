using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using LiteCodeGen;
using LiteGame.Editor.UiTools.Naming;
using UnityEngine;

namespace LiteGame.Editor.UiTools.Binding
{
    /// <summary>树行（窗口展示与批量操作的最小单元；由 <see cref="UiBindingRename.BuildTree"/> 产出）。</summary>
    public sealed class UiNodeRow
    {
        public Transform Node;            // 展示快照的节点（重建树后失效——按 Path 重解析）
        public string Path;               // 相对 prefab 根的路径（"Cot_A/Txt_B"）
        public string Name;
        public int Depth;
        public bool IsRoot;
        public bool HasBindNode;
        public string BindName;           // BindNode.BindName（无标记 = null）
        public string TypeName;           // BindNode.ResolvedTypeName（无标记 = null）
        public string Comment;
        public bool HasBindRoot;          // 子生成根（独立作用域——批量操作不越界）
        public bool InCloneScope;         // Clone_ 模板子树内（列表项绑定作用域——与页面作用域分开看）
    }

    /// <summary>一次改名的完整计划（§9.2"改名联动"：节点名与 BindName 同批同写）。</summary>
    public sealed class RenamePlan
    {
        public string Path;               // 目标节点（相对根）
        public string OldName;
        public string NewName;
        public bool HasBindNode;
        public string OldBindName;        // null = 无标记
        public string NewBindName;        // null = 无标记；空串 = 有标记但不设索引名（§9.4 模板不预设）
        public bool BindNameChanges => HasBindNode && OldBindName != NewBindName;
    }

    /// <summary>
    /// 绑定命名引擎（《UI编辑器工具专项设计》§2.1.2）：前缀组合、改名计划、校验与应用。
    ///
    /// 纯逻辑面——窗口只做交互壳，EditMode 用例直测此处。判据单源：
    /// 文法/词表 = <see cref="UiNamingWords"/>（步 2 单源）；同文纪律 = 《UI制作规范》§9.1/§9.5
    /// （组合出的名字一次写节点名与 BindName，构造性保证同文，不存在只改一处的手工路径）。
    ///
    /// 应用面由调用方选择写入通道：Prefab Stage 实例（Unity Undo 生效）或
    /// <c>PrefabUtility.LoadPrefabContents</c> 内容（资产写经 Pipeline 收口）——本类只改内存对象。
    /// </summary>
    public static class UiBindingRename
    {
        private static readonly Regex Grammar = new Regex(UiNamingWords.GrammarPattern);
        private static readonly Regex SemanticPattern = new Regex("^[A-Za-z][A-Za-z0-9]*$");

        /// <summary>全部可选前缀（下拉用；角色词 + 控件段 + 模板段 + 模板本体，与词表单源同序）。</summary>
        public static IEnumerable<string> AllPrefixes()
        {
            foreach (var p in UiNamingWords.RolePrefixes) yield return p;
            foreach (var p in UiNamingWords.ControlSegments) yield return p;
            foreach (var p in UiNamingWords.TemplateSegments) yield return p;
            foreach (var p in UiNamingWords.TemplatePrefixes) yield return p;
        }

        /// <summary>
        /// 组合全名 = <c>前缀 + 语义</c>（§9.5"选前缀、只填语义名"）。语义须 PascalCase 起头、
        /// 纯 ASCII 字母数字（全名单下划线由前缀自带，语义内不得再有下划线）。
        /// </summary>
        public static bool TryComposeName(string prefix, string semantic, out string fullName, out string reason)
        {
            fullName = null;
            if (string.IsNullOrEmpty(prefix) || !UiNamingWords.IsKnownPrefix(prefix))
            {
                reason = "前缀须取自词表（§9.3）";
                return false;
            }
            if (!SemanticPattern.IsMatch(semantic ?? string.Empty))
            {
                reason = "语义段须 PascalCase 起头、纯 ASCII 字母数字、不含下划线（§9.2）";
                return false;
            }
            fullName = prefix + semantic;
            reason = null;
            return true;
        }

        /// <summary>解析现名为「前缀 + 语义」（§9.5：已带前缀的节点回显解析为两控件）。</summary>
        public static bool TryDecomposeName(string name, out string prefix, out string semantic)
            => UiNamingWords.TryParse(name, out prefix, out semantic);

        /// <summary>全名是否过 §9.2 文法。</summary>
        public static bool MatchesGrammar(string name) => Grammar.IsMatch(name ?? string.Empty);

        /// <summary>
        /// 构建树行（深度优先、层级序——遍历稳定，窗口与批量预览共用同一顺序）。
        /// <see cref="UiNodeRow.InCloneScope"/>＝Clone_ 模板子树（列表项作用域与页面作用域分列，§9.4）。
        /// </summary>
        public static List<UiNodeRow> BuildTree(GameObject root)
        {
            var rows = new List<UiNodeRow>();
            if (root == null) return rows;
            Walk(root.transform, 0, false, rows);
            return rows;
        }

        private static void Walk(Transform t, int depth, bool inClone, List<UiNodeRow> rows)
        {
            var node = t.GetComponent<BindNode>();
            var bindRoot = t.GetComponent<BindRoot>();
            var row = new UiNodeRow
            {
                Node = t,
                Path = RelativePath(t),
                Name = t.name,
                Depth = depth,
                IsRoot = t.parent == null,
                HasBindNode = node != null,
                BindName = node != null ? node.BindName : null,
                TypeName = node != null ? node.ResolvedTypeName : null,
                Comment = node != null ? node.Comment : null,
                HasBindRoot = bindRoot != null,
                InCloneScope = inClone || t.name.StartsWith("Clone_", StringComparison.Ordinal),
            };
            rows.Add(row);
            foreach (Transform child in t) Walk(child, depth + 1, row.InCloneScope, rows);
        }

        /// <summary>
        /// 单节点改名计划。BindName 联动纪律（§9.1"与节点名同文"）：
        /// 有标记且原 BindName 非空 → 新 BindName = 新名（含原不同文的漂移面——构造性同文）；
        /// 有标记但 BindName 为空（模板标记/私有件）→ 保持为空（§9.4 不预设索引名）。
        /// </summary>
        public static RenamePlan PlanRename(Transform node, string newName)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            if (string.IsNullOrEmpty(newName)) throw new ArgumentException("新名不得为空", nameof(newName));

            var bind = node.GetComponent<BindNode>();
            string oldBind = bind != null ? bind.BindName : null;
            string newBind = null;
            if (bind != null)
                newBind = string.IsNullOrEmpty(oldBind) ? string.Empty : newName;

            return new RenamePlan
            {
                Path = RelativePath(node),
                OldName = node.name,
                NewName = newName,
                HasBindNode = bind != null,
                OldBindName = oldBind,
                NewBindName = newBind,
            };
        }

        /// <summary>批量"换前缀（保留语义）"计划：现名可解析出词表前缀且语义段合法时才参与。</summary>
        public static List<RenamePlan> PlanRetargetPrefix(IReadOnlyList<Transform> nodes, string newPrefix)
        {
            var plans = new List<RenamePlan>();
            foreach (var node in nodes)
            {
                if (!TryDecomposeName(node.name, out _, out string semantic)) continue;
                if (!TryComposeName(newPrefix, semantic, out string newName, out _)) continue;
                plans.Add(PlanRename(node, newName));
            }
            return plans;
        }

        /// <summary>
        /// 计划集合法性（应用前必查）：新名过文法；同父下新名不与现存兄弟/其他计划冲突
        /// （重名会同时砸 C# 成员名与 BindName 索引——在预览面挡住，不等到生成报错）。
        /// </summary>
        public static List<string> ValidatePlans(GameObject root, IReadOnlyList<RenamePlan> plans)
        {
            var errors = new List<string>();
            var pending = new Dictionary<string, string>(StringComparer.Ordinal);   // 同父路径 → 新名
            foreach (var plan in plans)
            {
                if (!MatchesGrammar(plan.NewName))
                    errors.Add($"新名不合 §9.2 文法：{plan.NewName}");
                var node = FindByPath(root, plan.Path);
                if (node == null)
                {
                    errors.Add($"目标节点不存在：{plan.Path}");
                    continue;
                }
                string parentKey = node.parent != null ? RelativePath(node.parent) : string.Empty;
                string key = parentKey + "|" + plan.NewName;
                if (pending.TryGetValue(key, out string other))
                    errors.Add($"重名冲突：{plan.Path} 与 {other} 都改为 {plan.NewName}");
                else
                    pending[key] = plan.Path;

                foreach (Transform sibling in node.parent)
                {
                    if (sibling == node || sibling.name != plan.NewName) continue;
                    bool covered = false;
                    foreach (var p in plans)
                        if (p.Path == RelativePath(sibling) && p.NewName != sibling.name) { covered = true; break; }
                    if (!covered) errors.Add($"新名已被现存兄弟占用：{plan.Path} → {plan.NewName}");
                }
            }
            return errors;
        }

        /// <summary>
        /// 应用计划集（只改内存对象；写入通道由调用方定）。返回应用条数；
        /// 目标缺失/中途异常即抛——半改状态由调用方的通道语义兜住（Stage=Undo 可退，资产=不落盘）。
        /// 按路径段数**降序**应用（深层先改）：父节点改名会使其后代按旧路径寻不到——同批父子计划不互踩。
        /// </summary>
        public static int ApplyPlans(GameObject root, IReadOnlyList<RenamePlan> plans)
        {
            var ordered = new List<RenamePlan>(plans);
            ordered.Sort((a, b) =>
            {
                int depthCompare = b.Path.Split('/').Length.CompareTo(a.Path.Split('/').Length);
                return depthCompare != 0 ? depthCompare : string.CompareOrdinal(a.Path, b.Path);
            });

            int applied = 0;
            foreach (var plan in ordered)
            {
                var node = FindByPath(root, plan.Path);
                if (node == null) throw new InvalidOperationException($"目标节点不存在：{plan.Path}");
                if (node.name != plan.NewName)
                {
                    node.name = plan.NewName;
                    applied++;
                }
                if (plan.HasBindNode)
                {
                    var bind = node.GetComponent<BindNode>();
                    if (bind != null && bind.BindName != plan.NewBindName)
                    {
                        bind.BindName = plan.NewBindName;
                        applied++;
                    }
                }
            }
            return applied;
        }

        /// <summary>按相对路径找节点（大小写敏感、'/' 分段——与生成/报告的定位口径一致）。</summary>
        public static Transform FindByPath(GameObject root, string path)
        {
            if (root == null || string.IsNullOrEmpty(path)) return root ? root.transform : null;
            var t = root.transform;
            foreach (var seg in path.Split('/'))
            {
                Transform next = null;
                foreach (Transform c in t)
                    if (c.name == seg) { next = c; break; }
                if (next == null) return null;
                t = next;
            }
            return t;
        }

        /// <summary>节点相对 prefab 根的路径（根自身为空串）。</summary>
        public static string RelativePath(Transform node)
        {
            var s = new List<string>();
            for (var t = node; t.parent != null; t = t.parent) s.Add(t.name);
            s.Reverse();
            return string.Join("/", s);
        }
    }
}
