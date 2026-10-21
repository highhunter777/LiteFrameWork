using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteGame.Toolchain
{
    public sealed class ToolRequest
    {
        public readonly string ToolId;
        public readonly bool DryRun;
        public readonly string[] AllowedOutputs;
        public readonly string Actor;
        public readonly string ProjectRoot;

        public ToolRequest(
            string toolId,
            bool dryRun = false,
            IEnumerable<string> allowedOutputs = null,
            string actor = "hub",
            string projectRoot = null)
        {
            ToolId = toolId ?? string.Empty;
            DryRun = dryRun;
            AllowedOutputs = allowedOutputs == null
                ? Array.Empty<string>()
                : allowedOutputs.Where(path => !string.IsNullOrEmpty(path)).ToArray();
            Actor = string.IsNullOrEmpty(actor) ? "hub" : actor;
            ProjectRoot = string.IsNullOrEmpty(projectRoot)
                ? ToolchainManifestLoader.ProjectRoot
                : projectRoot;
        }
    }
}
