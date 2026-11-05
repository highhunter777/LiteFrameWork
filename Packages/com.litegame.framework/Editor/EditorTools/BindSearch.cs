//------------------------------------------------------------
// LiteCodeGen - 独立版绑定代码生成
//------------------------------------------------------------
using System;
using System.Collections.Generic;
using UnityEngine;

namespace LiteCodeGen.EditorTools
{
    /// <summary>一次收集到的单个绑定标记信息。</summary>
    public sealed class BindMark
    {
        public BindNode Bind;
        public string MemberName;   // 生成到 C# 的成员名(=节点 GameObject 名)
        public string TypeFullName; // 生成到 C# 的类型全名
        public string Comment;      // 备注
        public int OrderIndex;      // 层级遍历顺序,保证生成结果稳定
    }

    /// <summary>
    /// 绑定标记收集（生成器域）：遍历语义复用 <see cref="BindSurfaceWalk"/> 单源——
    /// 根自身不入成员、嵌套 <c>BindRoot</c> 子树独立、<c>Clone_</c> 子树与模板（prefab 实例）边界不入本页；
    /// 本类保留生成器域特有的成员资格校验（C# 标识符合法性/关键字/重名）与层级遍历序（OrderIndex）。
    /// </summary>
    public static class BindSearch
    {
        public static List<BindMark> Search(Transform root, List<string> errors)
        {
            var marks = new List<BindMark>();
            if (root == null)
            {
                return marks;
            }

            var seenNames = new HashSet<string>();
            var order = 0;

            foreach (BindSurfaceNode node in BindSurfaceWalk.Walk(root.gameObject))
            {
                if (node.IsRoot) continue;                              // 根自身不是成员
                if (node.InCloneScope) continue;                        // 克隆源不属于本页接口面
                if (node.InsideNestedBindRoot) continue;                // 嵌套生成单位：根与子树都不入父列表
                if (node.InsideTemplate || node.TemplateAssetPath != null) continue;   // 模板实例（含根）属模板自己的生成

                var bind = node.Bind;
                if (bind == null)
                {
                    continue;
                }

                var name = node.Node.name;
                string reason;
                if (!IsValidIdentifier(name, out reason))
                {
                    errors.Add(string.Format("节点 '{0}' 不能作为成员名:{1}(可改节点名或改名后重新生成)", name, reason));
                }
                else if (!seenNames.Add(name))
                {
                    errors.Add(string.Format("存在重名节点 '{0}',生成的成员名会冲突,请改名", name));
                }
                else
                {
                    marks.Add(new BindMark
                    {
                        Bind = bind,
                        MemberName = name,
                        TypeFullName = bind.ResolvedTypeName,
                        Comment = bind.Comment,
                        OrderIndex = order++
                    });
                }
            }

            return marks;
        }

        // ---------------- C# 标识符校验 ----------------

        public static bool IsValidIdentifier(string name, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrEmpty(name))
            {
                reason = "名称为空";
                return false;
            }

            var first = name[0];
            if (!char.IsLetter(first) && first != '_')
            {
                reason = "必须以字母或下划线开头";
                return false;
            }

            for (var i = 1; i < name.Length; i++)
            {
                var c = name[i];
                if (!char.IsLetterOrDigit(c) && c != '_')
                {
                    reason = "只能包含字母/数字/下划线";
                    return false;
                }
            }

            if (IsCSharpKeyword(name))
            {
                reason = "是 C# 保留关键字";
                return false;
            }

            return true;
        }

        private static bool IsCSharpKeyword(string name)
        {
            return Keywords.Contains(name);
        }

        private static readonly HashSet<string> Keywords = new HashSet<string>
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const",
            "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit",
            "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int",
            "interface", "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out",
            "override", "params", "partial", "private", "protected", "public", "readonly", "ref", "return", "sbyte",
            "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
            "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile",
            "while"
        };
    }
}
