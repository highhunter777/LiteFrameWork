using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LiteGame.Toolchain
{
    public enum ToolWriteOutcome
    {
        Unchanged,
        Planned,
        Written,
    }

    public sealed class ToolWriteScope
    {
        private sealed class UndoEntry
        {
            public string FullPath;
            public byte[] BeforeBytes;
        }

        private readonly string _root;
        private readonly string _rootWithSeparator;
        private readonly string[] _allowedOutputs;
        private readonly List<ToolChange> _changes = new List<ToolChange>();
        private readonly List<UndoEntry> _undo = new List<UndoEntry>();

        public bool DryRun { get; private set; }

        public IReadOnlyList<ToolChange> Changes { get { return _changes; } }

        public bool CanUndo { get { return !DryRun && _undo.Count > 0; } }

        public ToolWriteScope(string projectRoot, IEnumerable<string> allowedOutputs, bool dryRun)
        {
            if (string.IsNullOrEmpty(projectRoot)) throw new ArgumentException("projectRoot missing");
            _root = Path.GetFullPath(projectRoot);
            _rootWithSeparator = _root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            _allowedOutputs = (allowedOutputs ?? Enumerable.Empty<string>())
                .Where(path => !string.IsNullOrEmpty(path))
                .Select(NormalizeRelative)
                .ToArray();
            DryRun = dryRun;
        }

        public ToolWriteOutcome WriteText(string relativePath, string content, string kind = "generated")
        {
            string relative = NormalizeRelative(relativePath);
            string fullPath = Resolve(relative);
            EnsureAllowed(relative);

            byte[] beforeBytes = File.Exists(fullPath) ? File.ReadAllBytes(fullPath) : null;
            byte[] afterBytes = new UTF8Encoding(false).GetBytes(content ?? string.Empty);
            string beforeHash = beforeBytes == null ? null : Hash(beforeBytes);
            string afterHash = Hash(afterBytes);
            if (beforeHash == afterHash) return ToolWriteOutcome.Unchanged;

            ToolChangeKind changeKind = beforeBytes == null ? ToolChangeKind.Created : ToolChangeKind.Updated;
            if (DryRun)
            {
                _changes.Add(new ToolChange(relative, changeKind, beforeHash, afterHash, false));
                return ToolWriteOutcome.Planned;
            }

            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllBytes(fullPath, afterBytes);
            _changes.Add(new ToolChange(relative, changeKind, beforeHash, afterHash, true));
            _undo.Add(new UndoEntry { FullPath = fullPath, BeforeBytes = beforeBytes });
            return ToolWriteOutcome.Written;
        }

        public ToolResult UndoResult()
        {
            if (DryRun)
            {
                return ToolResult.Aborted("预览模式没有可回滚的写入。");
            }
            if (_undo.Count == 0)
            {
                return ToolResult.Aborted("没有可回滚的写入（本次未写盘或已回滚）。");
            }

            var lines = new List<string>();
            for (int i = _undo.Count - 1; i >= 0; i--)
            {
                UndoEntry entry = _undo[i];
                if (entry.BeforeBytes == null)
                {
                    if (File.Exists(entry.FullPath)) File.Delete(entry.FullPath);
                    lines.Add("删除：" + entry.FullPath);
                }
                else
                {
                    string directory = Path.GetDirectoryName(entry.FullPath);
                    if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                    File.WriteAllBytes(entry.FullPath, entry.BeforeBytes);
                    lines.Add("恢复：" + entry.FullPath);
                }
            }

            int count = _undo.Count;
            _undo.Clear();
            _changes.Clear();
            return ToolResult.Success("已回滚 " + count + " 个文件", string.Join("\n", lines));
        }

        private string Resolve(string relativePath)
        {
            string full = Path.GetFullPath(Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(_rootWithSeparator, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(full, _root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("写入路径越界（必须在工程根内）：" + relativePath);
            }
            return full;
        }

        private void EnsureAllowed(string relativePath)
        {
            if (_allowedOutputs.Length == 0)
            {
                throw new InvalidOperationException("工具未声明 outputs，禁止写入：" + relativePath);
            }
            foreach (string allowed in _allowedOutputs)
            {
                if (allowed.EndsWith("/", StringComparison.Ordinal))
                {
                    if (relativePath.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) return;
                }
                else if (string.Equals(relativePath, allowed, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
            throw new InvalidOperationException("写入未在清单 outputs 声明：" + relativePath);
        }

        private static string NormalizeRelative(string path)
        {
            string normalized = (path ?? string.Empty).Replace('\\', '/').TrimStart('/');
            foreach (string segment in normalized.Split('/'))
            {
                if (segment == "..")
                {
                    throw new InvalidOperationException("写入路径不得包含 '..'：" + path);
                }
            }
            return normalized;
        }

        private static string Hash(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash) sb.Append(value.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
