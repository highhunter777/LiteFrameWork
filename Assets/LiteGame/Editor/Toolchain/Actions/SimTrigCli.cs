using System;
using System.Linq;

namespace LiteGame.Toolchain
{
    /// <summary>
    /// SimTrig 查表的 public CLI 入口（toolbox trig）：按工具清单 codegen.sim-trig 的
    /// outputs 组装写盘允许面，转调 <see cref="SimTrigToolActions.Run"/>（经
    /// <see cref="ToolWriteScope"/>），不绕过写盘契约直写产物。
    /// 返回单行 "OK: ..."/"FAILED: ..." 文本供 Python 侧映射退出码（失败也返回字符串）。
    /// </summary>
    public static class SimTrigCli
    {
        public const string ToolId = "codegen.sim-trig";

        /// <summary>生成（dryRun=false）或预览（dryRun=true）；报告为单行文本。</summary>
        public static string Run(bool dryRun)
        {
            ToolchainManifestLoadResult load = ToolchainManifestLoader.Load();
            if (!load.Ok)
            {
                return "FAILED: 工具清单不可用：" + string.Join("；", load.Errors);
            }

            ToolchainTool tool = load.Manifest.Tools
                .FirstOrDefault(candidate => candidate != null && candidate.id == ToolId);
            if (tool == null)
            {
                return "FAILED: 工具清单缺少 " + ToolId;
            }

            string[] outputs = tool.Outputs
                .Where(output => output != null && !string.IsNullOrEmpty(output.path))
                .Select(output => output.path)
                .ToArray();
            var request = new ToolRequest(
                ToolId, dryRun, outputs, "cli", ToolchainManifestLoader.ProjectRoot);
            ToolResult result = SimTrigToolActions.Run(request);
            return (result.Ok ? "OK: " : "FAILED: ") + result.Summary;
        }

        /// <summary>只读新鲜度判定（toolbox trig --check）：不写盘。</summary>
        public static bool IsUpToDate()
        {
            return LiteSim.Editor.SimTrigTableGenerator.IsUpToDate();
        }
    }
}
