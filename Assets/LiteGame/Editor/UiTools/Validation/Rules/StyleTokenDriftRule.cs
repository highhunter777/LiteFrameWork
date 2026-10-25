using System;
using System.Collections.Generic;
using System.IO;
using LiteClient.UI;
using LiteGame.EditorTools.UI;
using UnityEditor;

namespace LiteGame.Editor.UiTools.Validation.Rules
{
    /// <summary>
    /// 样式族：令牌源与主题资产一致性（《UI 编辑器工具链专项设计》§6/§8.1/§8.3）。
    /// 从上下文取 JSON/SO 路径（缺省走 <see cref="UiThemePaths"/>），按 JSON 重算期望快照并与盘上 SO 逐字段比对：
    /// 不一致＝Error（SO 过期或手改），文件缺失/JSON 非法＝Error，提示跑 `ui.style.tokens`。
    /// 判据单源复用 <see cref="UiThemeSnapshot"/>（与生成器的"是否需要写盘"同一个比较）。
    /// </summary>
    public sealed class StyleTokenDriftRule : IUiRule
    {
        public string RuleId => UiRuleIds.StyleTokenDrift;

        public string Family => "样式";

        public IEnumerable<UiIssue> Check(UiValidationContext context)
        {
            var issues = new List<UiIssue>();
            string jsonPath = string.IsNullOrEmpty(context.ThemeJsonPath)
                ? UiThemePaths.DefaultJsonPath : context.ThemeJsonPath;
            string assetPath = string.IsNullOrEmpty(context.ThemeAssetPath)
                ? UiThemePaths.DefaultAssetPath : context.ThemeAssetPath;

            string fullJson = UiThemePaths.FullPath(jsonPath);
            if (!File.Exists(fullJson))
            {
                issues.Add(Error(jsonPath, "令牌源不存在：" + jsonPath));
                return issues;
            }

            UiThemeTokens tokens;
            try
            {
                tokens = UiThemeTokenJson.Deserialize(File.ReadAllText(fullJson));
            }
            catch (Exception error)
            {
                issues.Add(Error(jsonPath, "令牌源 JSON 非法：" + error.Message));
                return issues;
            }

            IReadOnlyList<string> problems = UiThemeTokenValidator.Validate(tokens);
            if (problems.Count > 0)
            {
                foreach (string problem in problems) issues.Add(Error(jsonPath, problem));
                return issues;
            }

            UiThemeAsset asset = AssetDatabase.LoadAssetAtPath<UiThemeAsset>(assetPath);
            if (asset == null)
            {
                issues.Add(Error(assetPath, "主题资产不存在：" + assetPath + "（跑 ui.style.tokens 生成）"));
                return issues;
            }

            string sourceHash = UiThemeTokenJson.HashCanonical(UiThemeTokenJson.Serialize(tokens));
            UiThemeSnapshot expected = UiThemeSnapshot.FromTokens(tokens, sourceHash);
            string difference = expected.DescribeDifference(UiThemeSnapshot.FromAsset(asset));
            if (difference != null)
                issues.Add(Error(assetPath, "主题资产与令牌源不一致（SO 过期或手改：" + difference + "）"));

            return issues;
        }

        private UiIssue Error(string asset, string message)
            => new UiIssue
            {
                RuleId = RuleId,
                Severity = UiSeverity.Error,
                Asset = asset,
                Message = message,
                FixHint = "运行 ui.style.tokens 重新生成（Hub/CLI）",
            };
    }
}
