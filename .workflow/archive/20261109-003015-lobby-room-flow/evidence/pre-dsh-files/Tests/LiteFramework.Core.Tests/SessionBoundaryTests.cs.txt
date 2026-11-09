using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace LiteFramework.Core.Tests
{
    /// <summary>
    /// Session 域边界守卫（Session/Battle 解耦 · A1/A2）：会话域只做账号/连接/传输。
    /// </summary>
    public sealed class SessionBoundaryTests
    {
        /// <summary>Session 域禁用的 Battle 具体类型（端口名不在其列）。</summary>
        private static readonly string[] ConcreteBattleTypes =
        {
            "BattleContext",
            "BattleClient",
            "GameplayMatchIdentity",
        };

        /// <summary>A2 终态：Session 域零 Battle 具体类型（A1 的 2 文件过渡白名单已随搬迁清空）。</summary>
        private static readonly HashSet<string> A2Allowlist = new HashSet<string>(StringComparer.Ordinal);

        [Fact]
        public void AccountSessionFactory_不直接新建BattleClient()
        {
            string root = RepoRoot();
            const string relative = "Assets/LiteGame/Modules/Session/AccountSessionFactory.cs";
            string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path),
                "守卫目标文件不存在（搬迁后须同步更新守卫路径）：" + relative);

            string original = File.ReadAllText(path);
            string code = StripCommentsAndStrings(original);
            const string forbidden = "new BattleClient(";

            int line = LineNumberOf(original, forbidden);
            string location = line > 0 ? relative + ":" + line : relative;
            string snippet = line > 0 ? LineAt(original, line).Trim() : "(代码区)";
            Assert.True(code.IndexOf(forbidden, StringComparison.Ordinal) < 0,
                "Session 域不得直接构造战斗客户端（AC1）：" + location + " 命中 `" + forbidden + "` → " + snippet
                + "；Account 客户端须经 IBattleClientFactory 端口创建（Battle 域唯一 new BattleClient 处）。");
        }

        [Fact]
        public void Session域具体类型引用仅白名单()
        {
            string root = RepoRoot();
            string sessionRoot = Path.Combine(root, "Assets", "LiteGame", "Modules", "Session");
            Assert.True(Directory.Exists(sessionRoot),
                "Session 域目录不存在（搬迁后须同步更新守卫根路径）：Assets/LiteGame/Modules/Session");

            var offenders = new List<string>();
            foreach (string file in Directory.GetFiles(sessionRoot, "*.cs", SearchOption.AllDirectories))
            {
                string relative = Relative(root, file);
                if (A2Allowlist.Contains(relative)) continue;

                string code = StripCommentsAndStrings(File.ReadAllText(file));
                foreach (string type in ConcreteBattleTypes)
                {
                    if (Regex.IsMatch(code, "\\b" + Regex.Escape(type) + "\\b"))
                        offenders.Add(relative + " → " + type);
                }
            }

            offenders.Sort(StringComparer.Ordinal);
            Assert.True(offenders.Count == 0,
                "Session 域只经 Battle 端口协作（A2 终态：零 Battle 具体类型）；"
                + "白名单外的 Battle 具体类型引用：" + string.Join(" | ", offenders));
        }

        /// <summary>去注释/字符串（轻量词法：只服务守卫的"代码区"判据，与 ProcedureBoundaryTests 同款）。</summary>
        private static string StripCommentsAndStrings(string text)
        {
            text = Regex.Replace(text, @"/\*.*?\*/", " ", RegexOptions.Singleline);
            text = Regex.Replace(text, @"//.*?$", " ", RegexOptions.Multiline);
            text = Regex.Replace(text, "\"(?:\\\\.|[^\"\\\\])*\"", "\"\"");
            return text;
        }

        private static int LineNumberOf(string text, string needle)
        {
            int index = text.IndexOf(needle, StringComparison.Ordinal);
            if (index < 0) return -1;
            int line = 1;
            for (int i = 0; i < index; i++)
                if (text[i] == '\n') line++;
            return line;
        }

        private static string LineAt(string text, int line)
        {
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            return line >= 1 && line <= lines.Length ? lines[line - 1] : string.Empty;
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
