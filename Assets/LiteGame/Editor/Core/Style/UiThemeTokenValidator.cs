using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace LiteGame.EditorTools.UI
{
    /// <summary>
    /// 令牌源校验（《UI 编辑器工具链专项设计》§8.1；规则对齐 `UiStyleTokens.schema.json` 2.0 的约束面）：
    /// 七组必须存在、令牌 ID 文法与组归属、颜色格式、字号/行高/时长范围、字重枚举。
    /// 组清单只从 <see cref="UiThemeGroups.Enumerate"/> 取（新增组不散落在多个调用点）；一次收集全部问题。
    /// "必填但默认值合法"的字段（数值 value / 层级偏移 / 动效时长）由模型的 `Required.Always` 在反序列化时拒绝，
    /// 本类不重复实现存在性检查。
    /// </summary>
    public static class UiThemeTokenValidator
    {
        private static readonly Regex TokenIdPattern = new Regex(
            @"^(color|font|space|radius|elev|motion|ease|icon)\.[a-z0-9]+(\.[a-z0-9]+)*$",
            RegexOptions.CultureInvariant);

        private static readonly Regex SchemaVersionPattern = new Regex(@"^2\.[0-9]+$", RegexOptions.CultureInvariant);

        private static readonly Regex HexPattern = new Regex(
            @"^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$", RegexOptions.CultureInvariant);

        private static readonly HashSet<string> Weights =
            new HashSet<string>(StringComparer.Ordinal) { "Regular", "Medium", "Bold" };

        /// <summary>校验令牌源；返回问题列表（空＝通过）。</summary>
        public static IReadOnlyList<string> Validate(UiThemeTokens tokens)
        {
            var issues = new List<string>();
            if (tokens == null)
            {
                issues.Add("令牌源为空");
                return issues;
            }

            if (string.IsNullOrWhiteSpace(tokens.SchemaVersion) || !SchemaVersionPattern.IsMatch(tokens.SchemaVersion))
                issues.Add("schemaVersion 必须是 2.x：" + (tokens.SchemaVersion ?? "(缺失)"));

            ValidateMeta(tokens.Meta, issues);

            if (tokens.Groups == null)
            {
                issues.Add("groups 缺失（七组都必须存在）");
                return issues;
            }

            foreach ((string name, string[] prefixes, IUiThemeGroup group) in tokens.Groups.Enumerate())
            {
                if (group == null || !group.Present)
                {
                    issues.Add("groups." + name + " 缺失（七组都必须存在）");
                    continue;
                }

                foreach ((string id, object token) in group.Enumerate())
                {
                    string tokenId = id ?? "(空ID)";
                    if (!TokenIdPattern.IsMatch(tokenId))
                    {
                        issues.Add(tokenId + ": 令牌 ID 文法非法（小写点分层级）");
                        continue;
                    }
                    if (!prefixes.Any(prefix => tokenId.StartsWith(prefix, StringComparison.Ordinal)))
                    {
                        issues.Add(tokenId + ": 令牌 ID 不属于 " + name + " 组（前缀应为 "
                            + string.Join("/", prefixes) + "）");
                        continue;
                    }

                    ValidateToken(tokenId, token, issues);
                }
            }
            return issues;
        }

        private static void ValidateMeta(UiThemeMeta meta, List<string> issues)
        {
            if (meta == null)
            {
                issues.Add("meta 缺失");
                return;
            }
            if (string.IsNullOrWhiteSpace(meta.Name)) issues.Add("meta.name 不能为空");
            if (string.IsNullOrWhiteSpace(meta.Version)) issues.Add("meta.version 不能为空");
            if (string.IsNullOrWhiteSpace(meta.Theme)) issues.Add("meta.theme 不能为空");
        }

        private static void ValidateToken(string id, object token, List<string> issues)
        {
            switch (token)
            {
                case UiThemeColorToken color:
                    if (!HexPattern.IsMatch(color.Value ?? string.Empty))
                        issues.Add(id + ": 颜色格式非法（" + (color.Value ?? "(缺失)")
                            + "），须 #RRGGBB 或 #RRGGBBAA");
                    break;

                case UiThemeTypographyToken typography:
                    if (string.IsNullOrWhiteSpace(typography.FontFamily))
                        issues.Add(id + ": fontFamily 不能为空");
                    if (typography.FontSize < 8f || typography.FontSize > 96f)
                        issues.Add(id + ": fontSize 超出 8–96（" + typography.FontSize + "）");
                    if (typography.LineHeight < 1f || typography.LineHeight > 2f)
                        issues.Add(id + ": lineHeight 超出 1–2（" + typography.LineHeight + "）");
                    if (!Weights.Contains(typography.Weight ?? string.Empty))
                        issues.Add(id + ": weight 非法（" + (typography.Weight ?? "(缺失)")
                            + "），须 Regular/Medium/Bold");
                    break;

                case UiThemeNumberToken _:
                    break;   // value 由 Required.Always 保证存在；数值本身无额外范围约束

                case UiThemeElevationToken elevation:
                    if (!HexPattern.IsMatch(elevation.Color ?? string.Empty))
                        issues.Add(id + ": color 格式非法（" + (elevation.Color ?? "(缺失)") + "）");
                    if (elevation.Blur < 0f)
                        issues.Add(id + ": blur 不能为负（" + elevation.Blur + "）");
                    break;

                case UiThemeMotionToken motion:
                    if (motion.DurationMs < 0f || motion.DurationMs > 5000f)
                        issues.Add(id + ": durationMs 超出 0–5000（" + motion.DurationMs + "）");
                    break;

                default:
                    issues.Add(id + ": 未知令牌类型（" + (token?.GetType().Name ?? "null") + "）");
                    break;
            }
        }
    }
}
