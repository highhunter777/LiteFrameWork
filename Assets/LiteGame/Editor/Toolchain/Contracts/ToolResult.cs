using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteGame.Toolchain
{
    public enum ToolStatus
    {
        Ok,
        Warning,
        Aborted,
        Failed,
    }

    public enum ToolChangeKind
    {
        Created,
        Updated,
    }

    public sealed class ToolChange
    {
        public readonly string Path;
        public readonly ToolChangeKind Kind;
        public readonly string BeforeHash;
        public readonly string AfterHash;
        public readonly bool Written;

        public ToolChange(string path, ToolChangeKind kind, string beforeHash, string afterHash, bool written)
        {
            Path = path;
            Kind = kind;
            BeforeHash = beforeHash;
            AfterHash = afterHash;
            Written = written;
        }
    }

    public sealed class ToolResult
    {
        public readonly ToolStatus Status;
        public readonly string Summary;
        public readonly string Details;
        public readonly string FixHint;
        public readonly IReadOnlyList<ToolChange> Changes;
        public readonly bool DryRun;
        public readonly TimeSpan Duration;
        public readonly Func<ToolResult> Undo;

        private ToolResult(
            ToolStatus status,
            string summary,
            string details,
            string fixHint,
            IEnumerable<ToolChange> changes,
            bool dryRun,
            TimeSpan duration,
            Func<ToolResult> undo)
        {
            Status = status;
            Summary = summary ?? string.Empty;
            Details = details ?? string.Empty;
            FixHint = fixHint ?? string.Empty;
            Changes = changes == null ? Array.Empty<ToolChange>() : changes.ToArray();
            DryRun = dryRun;
            Duration = duration;
            Undo = undo;
        }

        public bool Ok
        {
            get { return Status == ToolStatus.Ok || Status == ToolStatus.Warning; }
        }

        public string DisplayStatus
        {
            get
            {
                switch (Status)
                {
                    case ToolStatus.Ok: return "完成";
                    case ToolStatus.Warning: return "完成（有提示）";
                    case ToolStatus.Aborted: return "中止";
                    case ToolStatus.Failed: return "失败";
                    default: return Status.ToString();
                }
            }
        }

        public static ToolResult Success(string summary, string details = null)
        {
            return new ToolResult(ToolStatus.Ok, summary, details, null, null, false, TimeSpan.Zero, null);
        }

        public static ToolResult Warning(string summary, string details = null, string fixHint = null)
        {
            return new ToolResult(ToolStatus.Warning, summary, details, fixHint, null, false, TimeSpan.Zero, null);
        }

        public static ToolResult Aborted(string summary, string details = null, string fixHint = null)
        {
            return new ToolResult(ToolStatus.Aborted, summary, details, fixHint, null, false, TimeSpan.Zero, null);
        }

        public static ToolResult Failed(string summary, string details = null)
        {
            return new ToolResult(ToolStatus.Failed, summary, details, null, null, false, TimeSpan.Zero, null);
        }

        public static ToolResult FailedWithScope(string summary, string details, ToolWriteScope scope, TimeSpan duration)
        {
            return new ToolResult(
                ToolStatus.Failed,
                summary,
                details,
                null,
                scope != null ? scope.Changes : null,
                scope != null && scope.DryRun,
                duration,
                scope != null && scope.CanUndo ? scope.UndoResult : null);
        }

        public static ToolResult FromLegacy(string report, ToolWriteScope scope, TimeSpan duration)
        {
            string text = report ?? string.Empty;
            string firstLine = text
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? string.Empty;
            bool aborted = text.TrimStart().StartsWith("ABORT", StringComparison.Ordinal);
            ToolStatus status = aborted ? ToolStatus.Aborted : ToolStatus.Ok;
            Func<ToolResult> undo = scope != null && scope.CanUndo ? scope.UndoResult : null;
            return new ToolResult(
                status,
                firstLine,
                text,
                aborted ? "查看报告中的 ABORT 原因，按提示修复后重试。" : null,
                scope != null ? scope.Changes : null,
                scope != null && scope.DryRun,
                duration,
                undo);
        }
    }
}
