using System.Collections.Generic;
using System.Text;

namespace LiteGame.Editor.UiTools.Validation
{
    /// <summary>违规严重级——对齐《UI编辑器工具专项设计》§4 报告契约（规则严重级别与制作规范一致）。</summary>
    public enum UiSeverity
    {
        Info,
        Warning,
        Error,
    }

    /// <summary>
    /// 单条校验问题（《UI编辑器工具专项设计》§4 统一报告项 `{ruleId,severity,asset,node,message,fixHint}`）。
    /// 纯数据——不依赖 Unity 类型，便于用例合成与 CI 序列化。
    /// </summary>
    public sealed class UiIssue
    {
        /// <summary>规则标识（稳定、来自规则定义，如 <c>ui.runtime-visual</c>）。</summary>
        public string RuleId;

        public UiSeverity Severity;

        /// <summary>资产/源码相对路径（可为空——规则级问题）。</summary>
        public string Asset;

        /// <summary>节点路径（资产内层级；源码面留空）。</summary>
        public string Node;

        /// <summary>人类可读的问题描述。</summary>
        public string Message;

        /// <summary>修复提示（§4 契约要求：出错要给出怎么改）。</summary>
        public string FixHint;

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append('[').Append(Severity).Append("] ").Append(RuleId);
            if (!string.IsNullOrEmpty(Asset)) sb.Append(" @ ").Append(Asset);
            if (!string.IsNullOrEmpty(Node)) sb.Append('#').Append(Node);
            sb.Append(" — ").Append(Message);
            if (!string.IsNullOrEmpty(FixHint)) sb.Append("（修：").Append(FixHint).Append('）');
            return sb.ToString();
        }
    }

    /// <summary>
    /// 一条规则族的登记项：名字 + 是否已接入实现。**未接入的族必须在报告里显式列出**——
    /// 《UI编辑器工具专项设计》§4 明文："未实现的规则标识为待接入，不显示为自动通过"。
    /// </summary>
    public sealed class UiRuleFamily
    {
        public string Family;
        public bool Implemented;
        public string Note;

        public UiRuleFamily(string family, bool implemented, string note = null)
        {
            Family = family;
            Implemented = implemented;
            Note = note;
        }
    }

    /// <summary>
    /// 校验报告（§4）：收集问题 + 汇总 + 文本渲染。菜单/CLI/测试共用同一报告形态。
    /// </summary>
    public sealed class UiReport
    {
        private readonly List<UiIssue> _issues = new List<UiIssue>();
        private readonly List<UiRuleFamily> _families = new List<UiRuleFamily>();

        public IReadOnlyList<UiIssue> Issues => _issues;
        public IReadOnlyList<UiRuleFamily> Families => _families;

        public int ErrorCount
        {
            get
            {
                int n = 0;
                foreach (var i in _issues) if (i.Severity == UiSeverity.Error) n++;
                return n;
            }
        }

        public int WarningCount
        {
            get
            {
                int n = 0;
                foreach (var i in _issues) if (i.Severity == UiSeverity.Warning) n++;
                return n;
            }
        }

        /// <summary>是否通过（无 Error——Warning 不拦）。</summary>
        public bool Passed => ErrorCount == 0;

        public void Add(UiIssue issue)
        {
            if (issue != null) _issues.Add(issue);
        }

        public void AddRange(IEnumerable<UiIssue> issues)
        {
            if (issues == null) return;
            foreach (var i in issues) if (i != null) _issues.Add(i);
        }

        /// <summary>登记规则族（含未接入族——用于报告"待接入"栏）。</summary>
        public void RegisterFamily(UiRuleFamily family)
        {
            if (family != null) _families.Add(family);
        }

        /// <summary>稳定排序：按 (ruleId, asset, node, message) 序数比较——报告与 CI 输出可复现。</summary>
        public void SortIssues()
        {
            _issues.Sort((a, b) =>
            {
                int cmp = string.CompareOrdinal(a.RuleId, b.RuleId);
                if (cmp != 0) return cmp;
                cmp = string.CompareOrdinal(a.Asset, b.Asset);
                if (cmp != 0) return cmp;
                cmp = string.CompareOrdinal(a.Node, b.Node);
                if (cmp != 0) return cmp;
                return string.CompareOrdinal(a.Message, b.Message);
            });
        }

        /// <summary>文本渲染（菜单 Debug.Log / CLI stdout 共用）。</summary>
        public string Render()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"UI 校验：{_issues.Count} 条问题（Error {ErrorCount} / Warning {WarningCount}）");
            foreach (var i in _issues) sb.AppendLine("  " + i);

            var pending = new List<UiRuleFamily>();
            foreach (var f in _families) if (!f.Implemented) pending.Add(f);
            if (pending.Count > 0)
            {
                sb.AppendLine("待接入规则族（未跑——不表示通过）：");
                foreach (var f in pending)
                    sb.AppendLine("  - " + f.Family + (string.IsNullOrEmpty(f.Note) ? "" : "：" + f.Note));
            }
            return sb.ToString();
        }
    }
}
