using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace LiteFramework.Core.Tests
{
    /// <summary>
    /// 表现层读模型守卫（《客户端架构重构演进路线图》§3.5 接缝 2 / §4.11 第 8 条 / M2 退出条件）：
    /// <c>App/Presentation/Hud</c> 的 HUD 驱动只经 <see cref="LiteGame.IBattleReadModel"/> 取事实，
    /// 不得直读 <c>SimWorldState</c> 的槽位/武器数组布局。唯一例外是适配器
    /// <c>BattleReadModel.cs</c>——它把 Sim 布局翻译成事实，是"唯一翻译点"。
    ///
    /// 与 <see cref="DevStaticReadAllowlistTests"/> 同款文件级基线：M2 已把阵亡/复活与击杀提示
    /// 迁到读模型，基线从此固定为 1 个文件（只减不增）。
    /// </summary>
    public sealed class PresentationReadModelGuardTests
    {
        /// <summary>允许直读 Sim 布局的文件（适配器单点；新增即失败）。</summary>
        private static readonly HashSet<string> Allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "BattleReadModel.cs",
        };

        [Fact]
        public void Hud驱动不得直读SimWorldState()
        {
            string root = RepoRoot();
            string dir = Path.Combine(root, "Assets", "LiteGame", "Modules", "Presentation", "Hud");
            Assert.True(Directory.Exists(dir), "读模型守卫目标目录不存在：" + dir);

            var offenders = new List<string>();
            foreach (string file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(file);
                if (Allowed.Contains(name)) continue;
                if (UsesSimWorldState(File.ReadAllLines(file))) offenders.Add(name);
            }

            Assert.True(offenders.Count == 0,
                "HUD 驱动必须经 IBattleReadModel 取事实（唯一翻译点是 BattleReadModel.cs）："
                + string.Join(" | ", offenders));
        }

        /// <summary>是否真使用 Sim 布局类型（注释里提到类型名不算——文档引用是说明，不是直读）。</summary>
        private static bool UsesSimWorldState(string[] lines)
        {
            foreach (string line in lines)
            {
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal)) continue;   // 含 XML 文档注释
                if (line.Contains("SimWorldState")) return true;
            }
            return false;
        }

        /// <summary>向上找含 Tests/Tests.slnx 的仓库根（与 DisciplineScannerTests 同款定位）。</summary>
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
