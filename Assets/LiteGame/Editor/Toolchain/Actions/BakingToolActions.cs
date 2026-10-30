using System;
using System.Diagnostics;
using LiteGame.EditorTools;

namespace LiteGame.Toolchain
{
    internal static class BakingToolActions
    {
        public static ToolResult RunBody(ToolRequest request)
        {
            return Run(request, BodyCylinderBaker.All);
        }

        public static ToolResult RunMuzzle(ToolRequest request)
        {
            return Run(request, MuzzleOffsetBaker.All);
        }

        public static ToolResult RunGrip(ToolRequest request)
        {
            return Run(request, WeaponGripBaker.All);
        }

        public static ToolResult RunRootMotion(ToolRequest request)
        {
            return Run(request, RootMotionBaker.All);
        }

        public static ToolResult RunMap(ToolRequest request)
        {
            return Run(request, SimMapBaker.BakeAll);
        }

        private static ToolResult Run(ToolRequest request, Func<ToolWriteScope, string> action)
        {
            var stopwatch = Stopwatch.StartNew();
            var scope = new ToolWriteScope(request.ProjectRoot, request.AllowedOutputs, request.DryRun);
            try
            {
                string report = action(scope);
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
