using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteGame.Toolchain
{
    public sealed class EditorToolDescriptor
    {
        public readonly ToolchainTool Tool;
        public Func<IEditorToolPage> CreatePage;
        public Action OpenWindow;
        public Func<ToolRequest, ToolResult> Execute;
        public string Error;

        public EditorToolDescriptor(ToolchainTool tool)
        {
            Tool = tool;
        }

        public string Id { get { return Tool.id; } }

        public string Title { get { return Tool.title; } }

        public string Summary { get { return Tool.summary; } }

        public string Kind { get { return Tool.kind; } }

        public string Plane { get { return Tool.plane; } }

        public string PlaneLabel { get { return ToolchainPlanes.Label(Tool.plane); } }

        public string Purpose { get { return Tool.purpose; } }

        public string PurposeLabel { get { return ToolchainPurposes.Label(Tool.purpose); } }

        public string RequiresLabel { get { return ToolchainPlanes.RequiresLabel(Tool); } }

        public bool IsEditorKind { get { return ToolchainKinds.IsEditor(Tool.kind); } }

        public bool SupportsDryRun { get { return Tool.SupportsDryRun; } }

        public bool SupportsFileUndo { get { return Tool.SupportsFileUndo; } }

        public string[] OutputPaths
        {
            get { return Tool.Outputs.Select(output => output.path).ToArray(); }
        }

        public bool NeedsConfirmation
        {
            get
            {
                if (Tool.destructive) return true;
                return Tool.Writes.Any(scope =>
                    scope == "asset" || scope == "source" || scope == "distribution");
            }
        }

        public string WritesLabel
        {
            get { return Tool.Writes.Length == 0 ? "—" : string.Join(", ", Tool.Writes); }
        }

        public string EntryText
        {
            get
            {
                if (Kind == ToolchainKinds.Hud) return "游戏内 F1（Hub 不执行）";
                if (IsEditorKind) return "python scripts/toolchain.py open " + Id;
                if (Tool.Command.Length > 0) return string.Join(" ", Tool.Command);
                return "—";
            }
        }

        public string DisplayKind { get { return ToolchainKinds.Label(Kind); } }
    }
}
