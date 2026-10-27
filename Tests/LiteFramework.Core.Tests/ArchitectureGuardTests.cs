using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace LiteFramework.Core.Tests
{
    /// <summary>
    /// 架构硬边守卫（《客户端架构重构演进路线图》§4.11 1～5）：asmdef 反向边一律失败。
    /// 规则来自 M0 六个接缝的目标形态——边一旦被"临时方便"加回去，架构收敛就静默失效。
    /// </summary>
    public sealed class ArchitectureGuardTests
    {
        [Fact]
        public void 硬边_Runtime不得引用View_App不得引用DevLocalServer()
        {
            string root = RepoRoot();

            // 《路线图》§4.2：LiteClient.Runtime 不引用 LiteView（表现经 IBattleView 端口进入）
            AssertNoReference(root, "Assets/LiteClient/Runtime/LiteClient.Runtime.asmdef", "LiteView");

            // §4.2/§3.6：LiteGame.App 的 release 依赖图不含 DevLocalServer / RoomServer.*
            // （本地传输经 ILocalTransportFactory，由 LiteGame.Dev 装配）
            AssertNoReference(root, "Assets/LiteGame/App/LiteGame.App.asmdef", "LiteGame.DevLocalServer");
            AssertNoReferencePrefix(root, "Assets/LiteGame/App/LiteGame.App.asmdef", "RoomServer.");
        }

        [Fact]
        public void 硬边_UI队列唯一实现()
        {
            // I4 / §4.11 10：除 IUiOperationQueue 的实现外，UI 运行时代码不得声明 Queue<...> 作为调度器。
            string root = RepoRoot();
            string uiRoot = Path.Combine(root, "Assets", "LiteClient", "UI");
            var offenders = new List<string>();
            var pattern = new Regex(@"\bQueue\s*<", RegexOptions.Compiled);

            foreach (string file in Directory.GetFiles(uiRoot, "*.cs", SearchOption.AllDirectories))
            {
                if (string.Equals(Path.GetFileName(file), "UiOperationQueue.cs", StringComparison.Ordinal)) continue;
                string rel = Path.GetRelativePath(root, file).Replace('\\', '/');
                foreach (string line in File.ReadAllLines(file))
                {
                    string trimmed = line.TrimStart();
                    if (trimmed.StartsWith("//", StringComparison.Ordinal)) continue;
                    if (!pattern.IsMatch(line)) continue;
                    offenders.Add(rel + " :: " + trimmed);
                }
            }

            Assert.True(offenders.Count == 0,
                "UI 队列必须走 IUiOperationQueue（唯一实现 UiOperationQueue<T>）："
                + string.Join(" | ", offenders));
        }

        [Fact]
        public void 硬边_静态门面只限已登记()
        {
            // I2 / §4.11 6：禁止新增 `public static class *Bridge/Facade`——现有三门面是受控存量，
            // 新门面一律走组合注入（端口/构造参数）。
            string root = RepoRoot();
            var allowed = new HashSet<string>(StringComparer.Ordinal)
            {
                "Assets/LiteGame/LuaBridge/Bridge.Data.cs",
                "Assets/LiteGame/App/Meta/MetaBridge.cs",
                "Assets/LiteGame/App/Room/RoomBridge.cs",
            };
            var pattern = new Regex(@"public\s+static\s+class\s+\w*(Bridge|Facade)\b", RegexOptions.Compiled);
            var offenders = new List<string>();

            foreach (string file in Directory.GetFiles(Path.Combine(root, "Assets", "LiteGame"), "*.cs",
                         SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (allowed.Contains(rel)) continue;
                foreach (string line in File.ReadAllLines(file))
                    if (pattern.IsMatch(line)) offenders.Add(rel + " :: " + line.Trim());
            }

            Assert.True(offenders.Count == 0,
                "禁止新增静态门面（走构造注入/端口；确需例外先在设计文档登记）：" + string.Join(" | ", offenders));
        }

        private static void AssertNoReference(string root, string asmdefRelative, string forbidden)
        {
            string text = File.ReadAllText(Path.Combine(root, asmdefRelative.Replace('/', Path.DirectorySeparatorChar)));
            Assert.False(text.Contains("\"" + forbidden + "\"", StringComparison.Ordinal),
                $"{asmdefRelative} 不得引用 {forbidden}（架构硬边）");
        }

        private static void AssertNoReferencePrefix(string root, string asmdefRelative, string forbiddenPrefix)
        {
            string text = File.ReadAllText(Path.Combine(root, asmdefRelative.Replace('/', Path.DirectorySeparatorChar)));
            Assert.False(text.Contains("\"" + forbiddenPrefix, StringComparison.Ordinal),
                $"{asmdefRelative} 不得引用 {forbiddenPrefix}*（架构硬边）");
        }

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
