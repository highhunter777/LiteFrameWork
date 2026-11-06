using System;
using System.Collections.Generic;
using System.IO;
using LiteFramework.UI;
using LiteGame.EditorTools.UI;
using UnityEditor;

namespace LiteGame.Editor.UITools.Validation.Rules
{
    /// <summary>
    /// 样式族：令牌源与主题资产一致性（《UI 编辑器工具链专项设计》§6/§8.1/§8.3）。
    /// </summary>
    public sealed class StyleTokenDriftRule : IUIRule
    {
        public string RuleId => UIRuleIds.StyleTokenDrift;

        public string Family => "样式";

        public IEnumerable<UIIssue> Check(UIValidationContext context)
        {
            var issues = new List<UIIssue>();
            string jsonPath = string.IsNullOrEmpty(context.ThemeJsonPath)
                ? LiteGame.EditorTools.UI.UIThemePaths.DefaultJsonPath : context.ThemeJsonPath;
            string assetPath = string.IsNullOrEmpty(context.ThemeAssetPath)
                ? LiteGame.EditorTools.UI.UIThemePaths.DefaultAssetPath : context.ThemeAssetPath;

            string fullJson = LiteGame.EditorTools.UI.UIThemePaths.FullPath(jsonPath);
            if (!File.Exists(fullJson))
            {
                issues.Add(Error(jsonPath, "令牌源不存在：" + jsonPath));
                return issues;
            }

            UIThemeTokens tokens;
            try
            {
                tokens = UIThemeTokenJson.Deserialize(File.ReadAllText(fullJson));
            }
            catch (Exception error)
            {
                issues.Add(Error(jsonPath, "令牌源 JSON 非法：" + error.Message));
                return issues;
            }

            IReadOnlyList<string> problems = UIThemeTokenValidator.Validate(tokens);
            if (problems.Count > 0)
            {
                foreach (string problem in problems) issues.Add(Error(jsonPath, problem));
                return issues;
            }

            UIThemeAsset asset = AssetDatabase.LoadAssetAtPath<UIThemeAsset>(assetPath);
            if (asset == null)
            {
                issues.Add(Error(assetPath, "主题资产不存在：" + assetPath + "（跑 ui.style.tokens 生成）"));
                return issues;
            }

            string sourceHash = UIThemeTokenJson.HashCanonical(UIThemeTokenJson.Serialize(tokens));
            UIThemeSnapshot expected = UIThemeSnapshot.FromTokens(tokens, sourceHash);
            string difference = expected.DescribeDifference(UIThemeSnapshot.FromAsset(asset));
            if (difference != null)
                issues.Add(Error(assetPath, "主题资产与令牌源不一致（SO 过期或手改：" + difference + "）"));

            return issues;
        }

        private UIIssue Error(string asset, string message)
            => new UIIssue
            {
                RuleId = RuleId,
                Severity = UISeverity.Error,
                Asset = asset,
                Message = message,
                FixHint = "运行 ui.style.tokens 重新生成（Hub/CLI）",
            };
    }
}
