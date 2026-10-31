using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace LiteFramework.Core.Tests
{
    /// <summary>
    /// 流程段边界守卫（批 5 · A24）：应用壳只编排，不得直引战斗/联机的内部件——
    /// 对局经 <c>IBattleRun</c>/<c>IBattleRunFactory</c>，进房经 <c>IMatchJoin</c>，联机入口经
    /// <c>IAccountSessionControl</c>/<c>IMetaConfigFacts</c>/<c>IAppFlow</c> 端口。
    ///
    /// 为什么需要：A24 前 <c>ProcedureBattle</c> 直接构造/持有 <c>BattleClient</c>/<c>BattleContext</c>，
    /// <c>ProcedureMatch</c> 直接泵 <c>BattleClient</c>——流程与战斗内部件编译期耦合，改战斗结构必动流程。
    /// 本守卫只扫**代码区**（注释里出现类型名不算引用，文档可以解释历史）。
    /// </summary>
    public sealed class ProcedureBoundaryTests
    {
        private static readonly string[] Forbidden =
        {
            "BattleClient",
            "BattleContext",
            "RoomSessionFace",
            "TeleportToAimCommand",
            "MetaEntryService",
            "MetaEntryConfig",
        };

        [Fact]
        public void 流程段不得直引战斗与联机内部件()
        {
            string root = RepoRoot();
            var offenders = new List<string>();

            foreach (string file in ProcedureFiles(root))
            {
                string code = StripCommentsAndStrings(File.ReadAllText(file));
                foreach (string type in Forbidden)
                {
                    if (Regex.IsMatch(code, "\\b" + Regex.Escape(type) + "\\b"))
                        offenders.Add(Relative(root, file) + " → " + type);
                }
            }

            Assert.True(offenders.Count == 0,
                "流程段只经端口编排（IBattleRun/IMatchJoin/IAccountSessionControl/IAppFlow）；直引：" +
                string.Join(" | ", offenders));
        }

        [Fact]
        public void 流程段对局编排必须经运行端口()
        {
            string root = RepoRoot();
            string battle = StripCommentsAndStrings(File.ReadAllText(Path.Combine(root,
                "Assets/LiteGame/Modules/Procedure/Stages/ProcedureBattle.cs")));

            Assert.Contains("IBattleRunFactory", battle);
            Assert.Contains("_run.Tick(", battle);
            Assert.Contains("_run.WaitStartedOrEndedAsync(", battle);
            Assert.DoesNotContain("BattleContext", battle);
        }

        private static IEnumerable<string> ProcedureFiles(string root)
        {
            string procedure = Path.Combine(root, "Assets", "LiteGame", "Modules", "Procedure");
            foreach (string file in Directory.GetFiles(procedure, "*.cs", SearchOption.AllDirectories))
                yield return file;
            string composition = Path.Combine(root, "Assets", "LiteGame", "App", "ProcedureComposition.cs");
            if (File.Exists(composition)) yield return composition;
        }

        /// <summary>去注释/字符串（轻量词法：只服务守卫的"代码区"判据）。</summary>
        private static string StripCommentsAndStrings(string text)
        {
            text = Regex.Replace(text, @"/\*.*?\*/", " ", RegexOptions.Singleline);
            text = Regex.Replace(text, @"//.*?$", " ", RegexOptions.Multiline);
            text = Regex.Replace(text, "\"(?:\\\\.|[^\"\\\\])*\"", "\"\"");
            return text;
        }

        private static string Relative(string root, string path)
            => path.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, '/').Replace('\\', '/');

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Tests", "Tests.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir.FullName;
        }
    }
}
