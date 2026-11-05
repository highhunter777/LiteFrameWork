using System;
using System.IO;
using LiteGame.Editor.UITools.Validation;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// P1-4 例外登记（《UI 编辑器工具链专项设计》§6.3）：命中降级并标注、不命中不动、通配、
    /// 过期失效且可见、非法条目报错、坏文件不静默。
    /// </summary>
    public sealed class UIRuleExceptionsEditModeTests
    {
        private static UIIssue Error(string ruleId = "ui.page-structure", string asset = "Assets/A.prefab",
            string node = "Root/X", string message = "缺 CanvasGroup") => new UIIssue
        {
            RuleId = ruleId,
            Severity = UISeverity.Error,
            Asset = asset,
            Node = node,
            Message = message,
            FixHint = "补组件",
        };

        private static UIReport ReportWith(params UIIssue[] issues)
        {
            var report = new UIReport();
            report.AddRange(issues);
            return report;
        }

        private static string TempFile(string json)
        {
            string path = Path.Combine(Path.GetTempPath(), "ui-rule-exceptions-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, json);
            return path;
        }

        [Test]
        public void 命中例外_降级并标注()
        {
            var report = ReportWith(Error(), Error(ruleId: "ui.binding-naming"));
            var set = UIRuleExceptionSet.Parse(@"{
  ""schemaVersion"": ""1.0"",
  ""exceptions"": [
    { ""ruleId"": ""ui.page-structure"", ""asset"": ""Assets/A.prefab"", ""node"": ""Root/X"",
      ""owner"": ""art.ui"", ""reason"": ""旧页面待迁移"", ""expiry"": ""2099-12-31"" }
  ]
}");

            set.Apply(report);

            Assert.AreEqual(1, report.ErrorCount, "不命中的问题仍是 Error");
            UIIssue hit = report.Issues[0];
            Assert.AreEqual(UISeverity.Warning, hit.Severity);
            StringAssert.Contains("例外：art.ui", hit.Message);
            StringAssert.Contains("2099-12-31", hit.Message);
            Assert.AreEqual(UISeverity.Error, report.Issues[1].Severity, "不命中的问题不得降级");
            CollectionAssert.IsEmpty(set.InvalidMessages);
            CollectionAssert.IsEmpty(set.ExpiredMessages);
        }

        [Test]
        public void 资产与节点通配_命中()
        {
            var report = ReportWith(
                Error(asset: "Assets/LiteGame/Modules/UI/Content/Widgets/Tmp_Dialog.prefab", node: "Root/Content"),
                Error(asset: "Assets/LiteGame/Modules/UI/Content/Widgets/Tmp_Toast.prefab", node: "Other"));
            var set = UIRuleExceptionSet.Parse(@"{
  ""exceptions"": [
    { ""ruleId"": ""ui.page-structure"", ""asset"": ""Assets/LiteGame/Modules/UI/Content/Widgets/Tmp_*.prefab"",
      ""node"": """", ""owner"": ""art.ui"", ""reason"": ""批量迁移"", ""expiry"": ""2099-12-31"" }
  ]
}");

            set.Apply(report);

            Assert.AreEqual(0, report.ErrorCount, "通配资产 + 空节点应命中全部");
            Assert.AreEqual(UISeverity.Warning, report.Issues[0].Severity);
            Assert.AreEqual(UISeverity.Warning, report.Issues[1].Severity, "asset 通配命中、node 为空＝任意节点应命中");
        }

        [Test]
        public void 过期例外_失效且报expired()
        {
            var report = ReportWith(Error());
            var set = UIRuleExceptionSet.Parse(@"{
  ""exceptions"": [
    { ""ruleId"": ""ui.page-structure"", ""asset"": ""Assets/A.prefab"", ""node"": ""Root/X"",
      ""owner"": ""art.ui"", ""reason"": ""过期测试"", ""expiry"": ""2020-01-01"" }
  ]
}");

            set.Apply(report);

            Assert.AreEqual(1, report.ErrorCount, "过期例外不得降级");
            Assert.AreEqual(UISeverity.Error, report.Issues[0].Severity);
            Assert.AreEqual(1, set.ExpiredMessages.Count);
            Assert.IsTrue(set.FindMatch(report.Issues[0]) == null);
            Assert.IsTrue(Exists(report, UIRuleIds.ExceptionExpired, UISeverity.Warning), "过期本身要可见");
        }

        [Test]
        public void 非法条目_报invalid()
        {
            var report = ReportWith(Error());
            var set = UIRuleExceptionSet.Parse(@"{
  ""exceptions"": [
    { ""ruleId"": ""ui.page-structure"", ""asset"": ""Assets/A.prefab"", ""owner"": """",
      ""reason"": ""缺 owner"", ""expiry"": ""2099-12-31"" },
    { ""ruleId"": ""ui.page-structure"", ""asset"": ""Assets/A.prefab"", ""owner"": ""art.ui"",
      ""reason"": ""日期非法"", ""expiry"": ""2099/12/31"" }
  ]
}");

            set.Apply(report);

            Assert.AreEqual(UISeverity.Error, report.Issues[0].Severity, "原问题不降级");
            Assert.AreEqual(2, set.InvalidMessages.Count);
            Assert.AreEqual(2, Count(report, UIRuleIds.ExceptionInvalid), "两条非法条目各报一条 Error");
        }

        [Test]
        public void 同一问题只降一级_Info不变()
        {
            var report = ReportWith(Error(), new UIIssue
            {
                RuleId = "ui.page-structure", Severity = UISeverity.Warning,
                Asset = "Assets/A.prefab", Node = "Root/X", Message = "警告项",
            });
            var set = UIRuleExceptionSet.Parse(@"{
  ""exceptions"": [
    { ""ruleId"": ""ui.page-structure"", ""asset"": ""Assets/A.prefab"", ""node"": ""Root/X"",
      ""owner"": ""art.ui"", ""reason"": ""降级测试"", ""expiry"": ""2099-12-31"" }
  ]
}");

            set.Apply(report);

            Assert.AreEqual(UISeverity.Warning, report.Issues[0].Severity, "Error→Warning");
            Assert.AreEqual(UISeverity.Info, report.Issues[1].Severity, "Warning→Info");
        }

        [Test]
        public void 文件读取_不存在为空_坏JSON报非法()
        {
            UIRuleExceptionSet missing = UIRuleExceptionSet.Load(Path.Combine(Path.GetTempPath(),
                "ui-rule-exceptions-missing-" + Guid.NewGuid().ToString("N") + ".json"));
            CollectionAssert.IsEmpty(missing.Entries);
            CollectionAssert.IsEmpty(missing.InvalidMessages);

            string bad = TempFile("{ not json");
            try
            {
                UIRuleExceptionSet set = UIRuleExceptionSet.Load(bad);
                Assert.AreEqual(1, set.InvalidMessages.Count, "坏 JSON 必须显式报非法，不得静默当无例外");
            }
            finally
            {
                File.Delete(bad);
            }
        }

        [Test]
        public void 日期边界_当天仍有效()
        {
            var entry = new UIRuleExceptionEntry
            {
                RuleId = UIRuleIds.PageStructure, Asset = "Assets/A.prefab", Owner = "art.ui",
                Reason = "边界", Expiry = "2026-10-23",
            };
            Assert.IsFalse(UIRuleExceptionSet.IsExpired(entry, new DateTime(2026, 10, 23)), "当天仍有效");
            Assert.IsTrue(UIRuleExceptionSet.IsExpired(entry, new DateTime(2026, 10, 24)), "次日起过期");
            Assert.IsNull(UIRuleExceptionSet.Validate(entry));
        }

        private static bool Exists(UIReport report, string ruleId, UISeverity severity)
            => Count(report, ruleId, severity) > 0;

        private static int Count(UIReport report, string ruleId, UISeverity? severity = null)
        {
            int n = 0;
            foreach (UIIssue issue in report.Issues)
                if (issue.RuleId == ruleId && (severity == null || issue.Severity == severity.Value)) n++;
            return n;
        }
    }
}
