using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;

namespace LiteGame.Editor.UITools.Validation
{
    /// <summary>
    /// 例外登记集合：加载 → 校验（字段/日期）→ 过期判定 → 对报告统一降级。
    /// </summary>
    public sealed class UIRuleExceptionSet
    {
        /// <summary>例外文件默认落点（编辑器目录，不进包）。</summary>
        public const string DefaultPath = "Assets/LiteGame/Editor/UITools/UIRuleExceptions.json";

        /// <summary>到期日格式（设计 §6.3「到期版本/日期」——v1.0 只支持日期）。</summary>
        public const string DateFormat = "yyyy-MM-dd";

        public readonly List<UIRuleExceptionEntry> Entries = new List<UIRuleExceptionEntry>();
        public readonly List<string> InvalidMessages = new List<string>();
        public readonly List<string> ExpiredMessages = new List<string>();

        /// <summary>读例外文件；文件不存在＝无例外（合法状态），解析失败＝一条非法消息。</summary>
        public static UIRuleExceptionSet Load(string assetPath = DefaultPath)
        {
            string full = FullPath(assetPath);
            if (!File.Exists(full)) return new UIRuleExceptionSet();
            try
            {
                return Parse(File.ReadAllText(full));
            }
            catch (Exception ex)
            {
                var set = new UIRuleExceptionSet();
                set.InvalidMessages.Add("例外文件不可读：" + ex.Message);
                return set;
            }
        }

        /// <summary>解析例外 JSON（可空/空串＝无例外；格式错误抛 <see cref="JsonException"/>）。</summary>
        public static UIRuleExceptionSet Parse(string json)
        {
            var set = new UIRuleExceptionSet();
            if (string.IsNullOrWhiteSpace(json)) return set;
            UIRuleExceptionsDocument document = JsonConvert.DeserializeObject<UIRuleExceptionsDocument>(json);
            if (document?.Exceptions != null)
                foreach (UIRuleExceptionEntry entry in document.Exceptions)
                    if (entry != null) set.Entries.Add(entry);
            return set;
        }

        /// <summary>对报告应用例外（命中降级 + 标注；非法/过期自身入报告）。</summary>
        public void Apply(UIReport report)
        {
            if (report == null) return;

            var invalid = new List<UIIssue>();
            var expired = new List<UIIssue>();
            foreach (UIRuleExceptionEntry entry in Entries)
            {
                string problem = Validate(entry);
                if (problem != null)
                {
                    InvalidMessages.Add(problem);
                    invalid.Add(InvalidIssue(entry, problem));
                    continue;
                }
                if (IsExpired(entry))
                {
                    ExpiredMessages.Add($"{entry.RuleId} @ {entry.Asset}（到期 {entry.Expiry}，Owner {entry.Owner}）");
                    expired.Add(ExpiredIssue(entry));
                }
            }

            var snapshot = new List<UIIssue>(report.Issues);
            foreach (UIIssue issue in snapshot)
            {
                UIRuleExceptionEntry match = FindMatch(issue);
                if (match == null) continue;
                issue.Severity = Downgrade(issue.Severity);
                issue.Message = (issue.Message ?? string.Empty) + $"（例外：{match.Owner}，到期 {match.Expiry}）";
            }

            report.AddRange(invalid);
            report.AddRange(expired);
        }

        /// <summary>条目字段校验；返回 null＝合法。</summary>
        public static string Validate(UIRuleExceptionEntry entry)
        {
            if (entry == null) return "空条目";
            if (string.IsNullOrWhiteSpace(entry.RuleId)) return "缺 ruleId";
            if (string.IsNullOrWhiteSpace(entry.Asset)) return "缺 asset";
            if (string.IsNullOrWhiteSpace(entry.Owner)) return "缺 owner";
            if (string.IsNullOrWhiteSpace(entry.Reason)) return "缺 reason";
            if (string.IsNullOrWhiteSpace(entry.Expiry)) return "缺 expiry";
            if (!DateTime.TryParseExact(entry.Expiry.Trim(), DateFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out _))
                return "expiry 不是 " + DateFormat + "：" + entry.Expiry;
            return null;
        }

        /// <summary>是否已过期（当天仍有效）。</summary>
        public static bool IsExpired(UIRuleExceptionEntry entry, DateTime? today = null)
        {
            if (entry == null || !DateTime.TryParseExact((entry.Expiry ?? string.Empty).Trim(), DateFormat,
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime expiry))
                return false;
            return (today ?? DateTime.Today).Date > expiry.Date;
        }

        /// <summary>找到命中的例外（第一条合法且未过期者）。</summary>
        public UIRuleExceptionEntry FindMatch(UIIssue issue)
        {
            if (issue == null) return null;
            foreach (UIRuleExceptionEntry entry in Entries)
            {
                if (Validate(entry) != null || IsExpired(entry)) continue;
                if (!string.Equals(entry.RuleId, issue.RuleId, StringComparison.Ordinal)) continue;
                if (!MatchesPattern(entry.Asset, issue.Asset)) continue;
                if (!string.IsNullOrEmpty(entry.Node) && !MatchesPattern(entry.Node, issue.Node)) continue;
                return entry;
            }
            return null;
        }

        /// <summary>通配：`*`＝任意串（任意位置，可多个）；无 `*` 时精确匹配。</summary>
        public static bool MatchesPattern(string pattern, string value)
        {
            if (string.IsNullOrEmpty(pattern)) return string.IsNullOrEmpty(value);
            value = value ?? string.Empty;
            if (pattern == "*") return true;
            if (!pattern.Contains("*")) return string.Equals(pattern, value, StringComparison.Ordinal);

            string[] parts = pattern.Split('*');
            int index = 0;
            if (parts[0].Length > 0)
            {
                if (!value.StartsWith(parts[0], StringComparison.Ordinal)) return false;
                index = parts[0].Length;
            }
            for (int i = 1; i < parts.Length - 1; i++)
            {
                if (parts[i].Length == 0) continue;
                int found = value.IndexOf(parts[i], index, StringComparison.Ordinal);
                if (found < 0) return false;
                index = found + parts[i].Length;
            }

            string last = parts[parts.Length - 1];
            if (last.Length == 0) return true;
            int tail = value.LastIndexOf(last, StringComparison.Ordinal);
            return tail >= index && tail + last.Length == value.Length;
        }

        /// <summary>降级一级：Error→Warning、Warning→Info、Info→Info。</summary>
        public static UISeverity Downgrade(UISeverity severity)
        {
            switch (severity)
            {
                case UISeverity.Error: return UISeverity.Warning;
                case UISeverity.Warning: return UISeverity.Info;
                default: return UISeverity.Info;
            }
        }

        private static UIIssue InvalidIssue(UIRuleExceptionEntry entry, string problem) => new UIIssue
        {
            RuleId = UIRuleIds.ExceptionInvalid,
            Severity = UISeverity.Error,
            Asset = entry?.Asset,
            Node = entry?.Node,
            Message = "例外登记无效：" + problem,
            FixHint = "补齐 ruleId/asset/owner/reason/expiry（" + DateFormat + "），或删除该条",
        };

        private static UIIssue ExpiredIssue(UIRuleExceptionEntry entry) => new UIIssue
        {
            RuleId = UIRuleIds.ExceptionExpired,
            Severity = UISeverity.Warning,
            Asset = entry.Asset,
            Node = entry.Node,
            Message = $"例外已过期（到期 {entry.Expiry}）：{entry.Reason}（Owner {entry.Owner}）",
            FixHint = "续期（改 expiry）或移除例外，问题回到原级别",
        };

        private static string FullPath(string assetPath)
        {
            string root = UIValidator.ProjectRoot;
            return Path.Combine(root, (assetPath ?? string.Empty).Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
