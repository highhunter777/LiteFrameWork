using System;
using System.IO;
using Xunit;

namespace LiteSim.Tests
{
    /// <summary>
    /// 基线重录工具：**常态不执行**，仅在环境变量 <c>LITESIM_RECORD_BASELINE=1</c> 时重写基线文件。
    ///
    /// 用法：<c>LITESIM_RECORD_BASELINE=1 dotnet test Tests/LiteSim.Core.Tests --filter 重录基线</c>
    /// 纪律：基线是确定性的**裁判**，重录必须说明原因（CI 会要求提交信息带 `[baseline]`，见 .github/workflows/ci.yml）。
    ///
    /// 写入**仓库源文件**（`Tests/LiteSim.Core.Tests/Baselines/`）并同步到输出目录——
    /// 用例读的是输出目录副本（csproj 负责拷贝），只写输出目录会丢。
    /// </summary>
    public sealed class BaselineRecorder
    {
        private static readonly string[] Files = { "IeeeBaseline.txt", "SimChecksum.txt" };

        [Fact]
        public void 重录基线()
        {
            if (Environment.GetEnvironmentVariable("LITESIM_RECORD_BASELINE") != "1")
            {
                return;   // 常态直接返回（不标记 Skip，避免正常跑批时出现噪声）
            }

            string srcDir = Path.Combine(RepoRoot(), "Tests", "LiteSim.Core.Tests", "Baselines");
            string outDir = Path.Combine(AppContext.BaseDirectory, "Baselines");
            Directory.CreateDirectory(srcDir);
            Directory.CreateDirectory(outDir);

            File.WriteAllText(Path.Combine(srcDir, "IeeeBaseline.txt"), IeeeBaselineSpec.BuildText());
            File.WriteAllText(Path.Combine(srcDir, "SimChecksum.txt"), SimChecksumBaselineSpec.BuildText());

            foreach (string f in Files)
            {
                string src = Path.Combine(srcDir, f);
                if (File.Exists(src)) File.Copy(src, Path.Combine(outDir, f), true);
            }

            Assert.True(File.Exists(Path.Combine(srcDir, "IeeeBaseline.txt")));
        }

        private static string RepoRoot()
        {
            for (var c = new DirectoryInfo(AppContext.BaseDirectory); c != null; c = c.Parent)
                if (Directory.Exists(Path.Combine(c.FullName, "Tests"))
                    && File.Exists(Path.Combine(c.FullName, "Tests", "Tests.slnx")))
                    return c.FullName;
            throw new DirectoryNotFoundException("找不到仓库根（需要 Tests/Tests.slnx）");
        }
    }
}
