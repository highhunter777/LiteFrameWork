using System;
using System.IO;
using LiteGame.Toolchain;
using LiteTesting;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    [Category(TestCategory.Contract)]
    public sealed class ToolWriteScopeEditModeTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "litegame-tool-write-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        private ToolWriteScope Scope(bool dryRun, params string[] allowed)
        {
            return new ToolWriteScope(_root, allowed.Length == 0 ? new[] { "out/" } : allowed, dryRun);
        }

        [Test]
        public void 预览_零写盘且记录计划变更()
        {
            ToolWriteScope scope = Scope(true);
            ToolWriteOutcome outcome = scope.WriteText("out/a.txt", "x");

            Assert.That(outcome, Is.EqualTo(ToolWriteOutcome.Planned));
            Assert.That(File.Exists(Path.Combine(_root, "out", "a.txt")), Is.False);
            Assert.That(scope.Changes.Count, Is.EqualTo(1));
            Assert.That(scope.Changes[0].Written, Is.False);
            Assert.That(scope.CanUndo, Is.False);
        }

        [Test]
        public void 写入_相同内容不重复记录()
        {
            ToolWriteScope scope = Scope(false);
            Assert.That(scope.WriteText("out/a.txt", "same"), Is.EqualTo(ToolWriteOutcome.Written));
            Assert.That(scope.WriteText("out/a.txt", "same"), Is.EqualTo(ToolWriteOutcome.Unchanged));
            Assert.That(scope.Changes.Count, Is.EqualTo(1));
            Assert.That(File.ReadAllText(Path.Combine(_root, "out", "a.txt")), Is.EqualTo("same"));
        }

        [Test]
        public void 回滚_新建文件删除_更新恢复旧内容()
        {
            ToolWriteScope scope = Scope(false);
            scope.WriteText("out/new.txt", "new");
            Assert.That(File.Exists(Path.Combine(_root, "out", "new.txt")), Is.True);

            string updated = Path.Combine(_root, "out", "updated.txt");
            File.WriteAllText(updated, "old");
            scope.WriteText("out/updated.txt", "new");

            ToolResult result = scope.UndoResult();
            Assert.That(result.Ok, Is.True);
            Assert.That(File.Exists(Path.Combine(_root, "out", "new.txt")), Is.False);
            Assert.That(File.ReadAllText(updated), Is.EqualTo("old"));
            Assert.That(scope.CanUndo, Is.False);
        }

        [Test]
        public void 越界与未声明路径被拒绝()
        {
            Assert.Throws<InvalidOperationException>(() => Scope(false).WriteText("out/../evil.txt", "x"));
            Assert.Throws<InvalidOperationException>(() => Scope(false).WriteText("other/a.txt", "x"));
        }

        [Test]
        public void 失败或ABORT结果_仍保留部分写入的Undo()
        {
            ToolWriteScope scope = Scope(false);
            scope.WriteText("out/a.txt", "x");

            ToolResult failed = ToolResult.FailedWithScope("boom", "details", scope, TimeSpan.Zero);
            Assert.That(failed.Status, Is.EqualTo(ToolStatus.Failed));
            Assert.That(failed.Undo, Is.Not.Null);
            Assert.That(failed.Undo().Ok, Is.True);
            Assert.That(File.Exists(Path.Combine(_root, "out", "a.txt")), Is.False);

            ToolWriteScope abortedScope = Scope(false);
            abortedScope.WriteText("out/b.txt", "y");
            ToolResult aborted = ToolResult.FromLegacy("ABORT partial", abortedScope, TimeSpan.Zero);
            Assert.That(aborted.Status, Is.EqualTo(ToolStatus.Aborted));
            Assert.That(aborted.Undo, Is.Not.Null);
        }

        [Test]
        public void 空outputs禁止写入()
        {
            var scope = new ToolWriteScope(_root, Array.Empty<string>(), false);
            Assert.Throws<InvalidOperationException>(() => scope.WriteText("out/a.txt", "x"));
        }
    }
}
