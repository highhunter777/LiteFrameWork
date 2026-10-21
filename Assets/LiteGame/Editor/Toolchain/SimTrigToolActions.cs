using System;
using System.Diagnostics;
using LiteSim.Editor;
using UnityEditor;

namespace LiteGame.Toolchain
{
    internal static class SimTrigToolActions
    {
        public static ToolResult Run(ToolRequest request)
        {
            var stopwatch = Stopwatch.StartNew();
            var scope = new ToolWriteScope(request.ProjectRoot, request.AllowedOutputs, request.DryRun);
            try
            {
                string content = SimTrigTableGenerator.BuildContent();
                ToolWriteOutcome outcome = scope.WriteText(SimTrigTableGenerator.OutputPath, content);
                if (outcome == ToolWriteOutcome.Written)
                {
                    AssetDatabase.ImportAsset(SimTrigTableGenerator.OutputPath);
                }

                string label = outcome == ToolWriteOutcome.Unchanged
                    ? "up to date："
                    : outcome == ToolWriteOutcome.Planned ? "planned：" : "written：";
                string report = label + SimTrigTableGenerator.OutputPath
                    + "（在 BuildHash 源集，需重跑 python scripts/codegen/gen-build-hash.py）。";
                return ToolResult.FromLegacy(report, scope, stopwatch.Elapsed);
            }
            catch (Exception exception)
            {
                return ToolResult.FailedWithScope(
                    exception.GetType().Name + ": " + exception.Message,
                    exception.ToString(),
                    scope,
                    stopwatch.Elapsed);
            }
        }
    }
}
