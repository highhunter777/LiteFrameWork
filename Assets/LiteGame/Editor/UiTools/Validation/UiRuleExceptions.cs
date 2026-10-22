using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;

namespace LiteGame.Editor.UiTools.Validation
{
    /// <summary>一条例外登记（《UI 编辑器工具链专项设计》§6.3 /《UI 制作规范》§255）。</summary>
    public sealed class UiRuleExceptionEntry
    {
        [JsonProperty("ruleId", Order = 0)]
        public string RuleId;

        [JsonProperty("asset", Order = 1)]
        public string Asset;

        /// <summary>节点路径；空＝该资产任意节点。</summary>
        [JsonProperty("node", Order = 2)]
        public string Node;

        [JsonProperty("owner", Order = 3)]
        public string Owner;

        [JsonProperty("reason", Order = 4)]
        public string Reason;

        /// <summary>到期日（`yyyy-MM-dd`）；当天仍有效。</summary>
        [JsonProperty("expiry", Order = 5)]
        public string Expiry;
    }

    /// <summary>例外文件（手工维护的配置，不是生成物）。</summary>
    public sealed class UiRuleExceptionsDocument
    {
        public const string CurrentSchemaVersion = "1.0";

        [JsonProperty("schemaVersion", Order = 0)]
        public string SchemaVersion = CurrentSchemaVersion;

        [JsonProperty("exceptions", Order = 1)]
        public List<UiRuleExceptionEntry> Exceptions = new List<UiRuleExceptionEntry>();
    }

    /// <summary>
    /// 例外登记集合：加载 → 校验（字段/日期）→ 过期判定 → 对报告统一降级。
    ///
    /// 语义（设计 §6.3）：**问题仍然出现**，只是不再阻断——`Error→Warning`、`Warning→Info`，
    /// 并在 message 末尾标注「例外：owner，到期 expiry」；过期或非法的例外自身报问题
    /// （`ui.rule-exception.expired` Warning / `ui.rule-exception.invalid` Error），不得静默通过。
    /// </summary>
    public sealed class UiRuleExceptionSet
    {
        /// <summary>例外文件默认落点（编辑器目录，不进包）。</summary>
        public const string DefaultPath = "Assets/LiteGame/Editor/UiTools/UiRuleExceptions.json";

        /// <summary>到期日格式（设计 §6.3「到期版本/日期」——v1.0 只支持日期）。</summary>
        public const string DateFormat = "yyyy-MM-dd";

        public readonly List<UiRuleExceptionEntry> Entries = new List<UiRuleExceptionEntry>();
        public readonly List<string> InvalidMessages = new List<string>();
        public readonly List<string> ExpiredMessages = new List<string>();

        /// <summary>读例外文件；文件不存在＝无例外（合法状态），解析失败＝一条非法消息。</summary>
        public static UiRuleExceptionSet Load(string assetPath = DefaultPath)
        {
            string full = FullPath(assetPath);
            if (!File.Exists(full)) return new UiRuleExceptionSet();
            try
            {
                return Parse(File.ReadAllText(full));
            }
            catch (Exception ex)
            {
                var set = new UiRuleExceptionSet();
                set.InvalidMessages.Add("例外文件不可读：" + ex.Message);
                return set;
            }
        }

        /// <summary>解析例外 JSON（可空/空串＝无例外；格式错误抛 <see cref="JsonException"/>）。</summary>
        public static UiRuleExceptionSet Parse(string json)
        {
            var set = new UiRuleExceptionSet();
            if (string.IsNullOrWhiteSpace(json)) return set;
            UiRuleExceptionsDocument document = JsonConvert.DeserializeObject<UiRuleExceptionsDocument>(json);
            if (document?.Exceptions != null)
                foreach (UiRuleExceptionEntry entry in document.Exceptions)
                    if (entry != null) set.Entries.Add(entry);
            return set;
        }

        /// <summary>对报告应用例外（命中降级 + 标注；非法/过期自身入报告）。</summary>
        public void Apply(UiReport report)
        {
            if (report == null) return;

            var invalid = new List<UiIssue>();
            var expired = new List<UiIssue>();
            foreach (UiRuleExceptionEntry entry in Entries)
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

            var snapshot = new List<UiIssue>(report.Issues);
            foreach (UiIssue issue in snapshot)
            {
                UiRuleExceptionEntry match = FindMatch(issue);
                if (match == null) continue;
                issue.Severity = Downgrade(issue.Severity);
                issue.Message = (issue.Message ?? string.Empty) + $"（例外：{match.Owner}，到期 {match.Expiry}）";
            }

            report.AddRange(invalid);
            report.AddRange(expired);
        }

        /// <summary>条目字段校验；返回 null＝合法。</summary>
        public static string Validate(UiRuleExceptionEntry entry)
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
        public static bool IsExpired(UiRuleExceptionEntry entry, DateTime? today = null)
        {
            if (entry == null || !DateTime.TryParseExact((entry.Expiry ?? string.Empty).Trim(), DateFormat,
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime expiry))
                return false;
            return (today ?? DateTime.Today).Date > expiry.Date;
        }

        /// <summary>找到命中的例外（第一条合法且未过期者）。</summary>
        public UiRuleExceptionEntry FindMatch(UiIssue issue)
        {
            if (issue == null) return null;
            foreach (UiRuleExceptionEntry entry in Entries)
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
        public static UiSeverity Downgrade(UiSeverity severity)
        {
            switch (severity)
            {
                case UiSeverity.Error: return UiSeverity.Warning;
                case UiSeverity.Warning: return UiSeverity.Info;
                default: return UiSeverity.Info;
            }
        }

        private static UiIssue InvalidIssue(UiRuleExceptionEntry entry, string problem) => new UiIssue
        {
            RuleId = UiRuleIds.ExceptionInvalid,
            Severity = UiSeverity.Error,
            Asset = entry?.Asset,
            Node = entry?.Node,
            Message = "例外登记无效：" + problem,
            FixHint = "补齐 ruleId/asset/owner/reason/expiry（" + DateFormat + "），或删除该条",
        };

        private static UiIssue ExpiredIssue(UiRuleExceptionEntry entry) => new UiIssue
        {
            RuleId = UiRuleIds.ExceptionExpired,
            Severity = UiSeverity.Warning,
            Asset = entry.Asset,
            Node = entry.Node,
            Message = $"例外已过期（到期 {entry.Expiry}）：{entry.Reason}（Owner {entry.Owner}）",
            FixHint = "续期（改 expiry）或移除例外，问题回到原级别",
        };

        private static string FullPath(string assetPath)
        {
            string root = UiValidator.ProjectRoot;
            return Path.Combine(root, (assetPath ?? string.Empty).Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
