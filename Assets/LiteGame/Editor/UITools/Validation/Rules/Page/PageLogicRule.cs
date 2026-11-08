using System;
using System.Collections.Generic;
using LiteFramework.UI;
using LiteGame.Editor.UITools.Page;
using LiteGame.Editor.UITools.Reference;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Editor.UITools.Validation.Rules
{
    /// <summary>
    /// 契约族：页面逻辑件与 prefab 载体对账（《Lua 退出专项设计》§4.7）——逻辑件存在则 prefab 必须挂载体，
    /// 重复实现报 Error；迁移期允许"无逻辑件的页"，P2a 由 <see cref="RequireLogicForEveryPage"/> 收紧。
    /// </summary>
    public sealed class PageLogicRule : IUIRule
    {
        /// <summary>迁移期严格度：全部产品页都要求逻辑件（P2a 打开；打开前仅对已有逻辑件对账）。</summary>
        internal const bool RequireLogicForEveryPage = false;

        public string RuleId => UIRuleIds.PageLogic;
        public string Family => "契约";

        public IEnumerable<UIIssue> Check(UIValidationContext context)
        {
            var issues = new List<UIIssue>();
            string manifestPath = context.PageManifestPath ?? UIPageManifestPaths.DefaultPath;
            UIPageManifest manifest;
            try
            {
                manifest = UIPageManifestStore.Load(manifestPath);
            }
            catch (Exception ex)
            {
                issues.Add(new UIIssue
                {
                    RuleId = RuleId,
                    Severity = UISeverity.Error,
                    Asset = manifestPath,
                    Message = "页面清单读取失败：" + ex.Message,
                    FixHint = "重跑 ui.page-manifest 对账",
                });
                return issues;
            }

            foreach (UIPageManifestEntry entry in manifest.Pages ?? new List<UIPageManifestEntry>())
            {
                if (entry == null || string.IsNullOrEmpty(entry.Name)) continue;
                if (IsSystemPage(entry.Name)) continue;          // Tmp_/Hud_ 系统面不挂页面逻辑

                IReadOnlyList<string> files = UIPageLogicScanner.ListCSharpFiles(entry.Name);
                if (files.Count > 1)
                {
                    issues.Add(new UIIssue
                    {
                        RuleId = RuleId,
                        Severity = UISeverity.Error,
                        Asset = files[1],
                        Message = "页面逻辑件重复实现：" + entry.Name + "（" + files.Count + " 份）",
                        FixHint = "同名逻辑件只留一份（其余改类名或删除）；按名解析取排序首个，重复会静默选错",
                    });
                }

                bool carried = CarriesLogic(entry.PrefabPath);
                bool hasFile = files.Count > 0;

                if (hasFile && !carried)
                {
                    issues.Add(new UIIssue
                    {
                        RuleId = RuleId,
                        Severity = RequireLogicForEveryPage ? UISeverity.Error : UISeverity.Warning,
                        Asset = entry.PrefabPath,
                        Message = "页面逻辑件存在但 prefab 未挂载体：" + entry.Name
                            + "（打开会落 NullUIFormLogic——逻辑静默不执行）",
                        FixHint = "把 " + System.IO.Path.GetFileName(files[0]) + " 挂到 prefab 根（重跑 ui.validate 复核）",
                    });
                }
                else if (!hasFile && carried)
                {
                    issues.Add(new UIIssue
                    {
                        RuleId = RuleId,
                        Severity = UISeverity.Warning,
                        Asset = entry.PrefabPath,
                        Message = "prefab 挂了页面逻辑组件但找不到源文件：" + entry.Name,
                        FixHint = "确认逻辑件在 Modules/UI/Logic 下且文件名 = Page_<Name>Logic.cs",
                    });
                }
                else if (RequireLogicForEveryPage && !hasFile && !carried)
                {
                    issues.Add(new UIIssue
                    {
                        RuleId = RuleId,
                        Severity = UISeverity.Error,
                        Asset = entry.PrefabPath,
                        Message = "页面缺逻辑件：" + entry.Name,
                        FixHint = "补 Logic/**/Page_<Name>Logic.cs 并挂到 prefab 根",
                    });
                }
            }
            return issues;
        }

        /// <summary>系统面/模板页豁免（Tmp_ 模板、Hud_ 战斗面不走页面逻辑）。</summary>
        private static bool IsSystemPage(string pageName)
            => pageName.StartsWith("Tmp_", StringComparison.Ordinal)
               || pageName.StartsWith("Hud_", StringComparison.Ordinal);

        private static bool CarriesLogic(string prefabPath)
        {
            if (string.IsNullOrEmpty(prefabPath)) return false;
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            return prefab != null && prefab.GetComponentInChildren<IUIFormLogic>(true) != null;
        }
    }
}
