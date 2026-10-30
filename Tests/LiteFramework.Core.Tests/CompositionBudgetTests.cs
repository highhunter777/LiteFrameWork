using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace LiteFramework.Core.Tests
{
    /// <summary>
    /// 装配段形态守卫：模块装配件必须"就地"住进对应域目录（`Modules/&lt;域&gt;/&lt;域&gt;Composition.cs`），
    /// 且装配体本身足够薄（类 ≤ 400 行、方法 ≤ 80 行）。
    ///
    /// 为什么需要：13 个 `IClientModule` 实现在 `App/Bootstrap/Modules/`、功能域又在 `Modules/`——两处同名"模块"
    /// 导致"模块内实现只与自己有关"无法成为可校验的不变量；`ContainerModule.Assemble` 曾经 152 行，
    /// 是"装配根冒充功能模块"的直接证据。
    ///
    /// 判据（逐批推进，断言目标为**当前批完成后的结构**）：
    /// - `App/Bootstrap/Modules/` 不存在（批 1 后）；
    /// - `Modules/**` 至少 13 个 `*Composition.cs`（批 1 后），且不存在 `*Module.cs`（`App/ContainerModule.cs` 为过渡例外）；
    /// - 每个装配段文件：类声明 ≤ 400 行、任一方法 ≤ 80 行（与 `ArchitectureBudgetTests` 同阈值）。
    /// </summary>
    public sealed class CompositionBudgetTests
    {
        /// <summary>批 1 完成后应在位的最小装配段数量（12 段就地；Ui 段待 UI 逻辑工厂端口后归位）。</summary>
        private const int MinimumCompositions = 12;   // 13 段中 UiComposition 待 IUiLogicFactory 端口（过渡期住 App/）

        private const int MaxClassLines = 400;
        private const int MaxMethodLines = 80;

        [Fact]
        public void 模块目录不得残留于Bootstrap()
        {
            string root = RepoRoot();
            string legacy = Path.Combine(root, "Assets", "LiteGame", "App", "Bootstrap", "Modules");

            Assert.False(Directory.Exists(legacy),
                "装配段必须就地住进 Modules/<域>/<域>Composition.cs；`" + Relative(root, legacy) + "` 不应存在（批 1）");
        }

        [Fact]
        public void 装配段必须就地且数量达标()
        {
            string root = RepoRoot();
            string modules = Path.Combine(root, "Assets", "LiteGame", "Modules");
            Assert.True(Directory.Exists(modules), "Modules 目录缺失：" + modules);

            var compositions = Directory
                .GetFiles(modules, "*Composition.cs", SearchOption.AllDirectories)
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToArray();

            Assert.True(compositions.Length >= MinimumCompositions,
                "`Modules/**` 下装配段不足 " + MinimumCompositions + " 个（实测 " + compositions.Length + "）："
                + string.Join(", ", compositions.Select(p => Relative(root, p))));

            var strays = Directory
                .GetFiles(modules, "*Module.cs", SearchOption.AllDirectories)
                .Select(p => Relative(root, p))
                .ToArray();

            Assert.True(strays.Length == 0,
                "`Modules/**` 下不得再有 `*Module.cs`（应改名为 `<域>Composition.cs`）：" + string.Join(", ", strays));
        }

        [Fact]
        public void 装配段必须保持薄()
        {
            string root = RepoRoot();
            string modules = Path.Combine(root, "Assets", "LiteGame", "Modules");
            string app = Path.Combine(root, "Assets", "LiteGame", "App", "AppComposition.cs");

            var files = Directory.GetFiles(modules, "*Composition.cs", SearchOption.AllDirectories).ToList();
            if (File.Exists(app)) files.Add(app);   // 组合根同受预算约束（批 4）

            var offenders = new List<string>();
            foreach (string file in files)
            {
                string[] lines = File.ReadAllLines(file);
                foreach (var block in MethodBlocks(lines))
                {
                    int length = block.Value;
                    if (length > MaxMethodLines)
                        offenders.Add(Relative(root, file) + "::" + block.Key + "=" + length);
                }
            }

            Assert.True(offenders.Count == 0,
                "装配段方法超过 " + MaxMethodLines + " 行（须拆方法）：" + string.Join(" | ", offenders));
        }

        /// <summary>粗略方法切分：以 `public/private/internal/protected ...(` 起始，按大括号配平计行数。</summary>
        private static IEnumerable<KeyValuePair<string, int>> MethodBlocks(string[] lines)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].TrimStart();
                if (!(trimmed.StartsWith("public ", StringComparison.Ordinal)
                      || trimmed.StartsWith("private ", StringComparison.Ordinal)
                      || trimmed.StartsWith("internal ", StringComparison.Ordinal)
                      || trimmed.StartsWith("protected ", StringComparison.Ordinal)))
                    continue;

                if (!trimmed.Contains("(")) continue;
                if (trimmed.Contains("=>")) continue;          // 表达式体：不计
                if (trimmed.Contains(" class ") || trimmed.Contains(" struct ") || trimmed.Contains(" interface ")) continue;

                int depth = 0;
                int start = i;
                int end = i;
                for (int j = i; j < lines.Length; j++)
                {
                    foreach (char c in lines[j])
                    {
                        if (c == '{') depth++;
                        else if (c == '}') depth--;
                    }
                    if (j > i && depth <= 0) { end = j; break; }
                    if (j == i && depth == 0) { end = j; break; }   // 声明未开括号（同行闭合）
                }

                string name = trimmed;
                int paren = name.IndexOf('(');
                if (paren > 0) name = name.Substring(0, paren);
                yield return new KeyValuePair<string, int>(name.Trim(), end - start + 1);
                i = end;
            }
        }

        private static string Relative(string root, string path)
            => path.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, '/').Replace('\\', '/');

        /// <summary>向上找含 Tests/Tests.slnx 的仓库根（与既有守卫同款定位）。</summary>
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
