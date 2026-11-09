using System;
using System.Threading;
using System.Threading.Tasks;
using LiteFramework;
using Xunit;

namespace LiteFramework.Core.Tests
{
    /// <summary>
    /// M3-5 操作作用域（《客户端依赖注入专项设计》§6.1）：取消/超时/合并键/所有者/状态。
    /// 纯 BCL 语义，L1 直测。
    /// </summary>
    public sealed class OperationScopeTests
    {
        [Fact]
        public void Start_空Owner抛()
        {
            Assert.Throws<ArgumentException>(() => OperationScope.Start(" "));
        }

        [Fact]
        public void Start_非正超时抛()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => OperationScope.Start("t", timeout: TimeSpan.Zero));
        }

        [Fact]
        public void 初始态_Pending_元数据保留()
        {
            using var scope = OperationScope.Start("ui.open", "form:7", TimeSpan.FromSeconds(5));
            Assert.Equal(OperationState.Pending, scope.State);
            Assert.Equal("ui.open", scope.Owner);
            Assert.Equal("form:7", scope.MergeKey);
            Assert.Equal(TimeSpan.FromSeconds(5), scope.Timeout);
            Assert.False(scope.Token.IsCancellationRequested);
        }

        [Fact]
        public void 状态迁移_Running到Succeeded_终态后Cancel不改状态()
        {
            var scope = OperationScope.Start("ui.open");
            scope.MarkRunning();
            Assert.Equal(OperationState.Running, scope.State);
            scope.MarkSucceeded();
            Assert.True(scope.IsTerminal);
            Assert.Equal(OperationState.Succeeded, scope.State);

            scope.Cancel();
            Assert.Equal(OperationState.Succeeded, scope.State);
            Assert.False(scope.Token.IsCancellationRequested);
            scope.Dispose();
        }

        [Fact]
        public void Cancel_取消令牌并置Canceled_幂等()
        {
            var scope = OperationScope.Start("ui.nav");
            scope.MarkRunning();
            scope.Cancel();
            Assert.True(scope.Token.IsCancellationRequested);
            Assert.Equal(OperationState.Canceled, scope.State);

            scope.Cancel();
            Assert.Equal(OperationState.Canceled, scope.State);
            scope.Dispose();
        }

        [Fact]
        public void Dispose_未终态先取消_令牌可观测()
        {
            var scope = OperationScope.Start("ui.nav");
            scope.Dispose();
            Assert.True(scope.IsDisposed);
            Assert.True(scope.Token.IsCancellationRequested);
            Assert.Equal(OperationState.Canceled, scope.State);
        }

        [Fact]
        public async Task 超时_到点取消令牌且状态为Canceled()
        {
            var scope = OperationScope.Start("content.download", timeout: TimeSpan.FromMilliseconds(50));
            Assert.False(scope.Token.IsCancellationRequested);

            await Task.Delay(400);
            Assert.True(scope.Token.IsCancellationRequested);
            Assert.True(scope.IsTimedOut);
            Assert.Equal(OperationState.Canceled, scope.State);
            scope.Dispose();
        }

        [Fact]
        public void 显式取消_不算超时()
        {
            var scope = OperationScope.Start("content.fetch", timeout: TimeSpan.FromSeconds(30));
            scope.Cancel();
            Assert.True(scope.Token.IsCancellationRequested);
            Assert.False(scope.IsTimedOut);
            scope.Dispose();
        }

        [Fact]
        public async Task 多父令牌_任一取消即级联()
        {
            using var caller = new CancellationTokenSource();
            using var lifetime = new CancellationTokenSource();
            var scope = OperationScope.StartLinked("scene.load", "single:x", null, caller.Token, lifetime.Token);

            lifetime.Cancel();
            await Task.Yield();
            Assert.True(scope.Token.IsCancellationRequested);

            var second = OperationScope.StartLinked("scene.load", "single:y", null, caller.Token, CancellationToken.None);
            caller.Cancel();
            await Task.Yield();
            Assert.True(second.Token.IsCancellationRequested);

            scope.Dispose();
            second.Dispose();
        }

        [Fact]
        public async Task 父令牌_取消级联()
        {
            using var parent = new CancellationTokenSource();
            var scope = OperationScope.Start("ui.open", parent: parent.Token);
            parent.Cancel();
            await Task.Yield();

            Assert.True(scope.Token.IsCancellationRequested);
            Assert.Equal(OperationState.Canceled, scope.State);
            scope.Dispose();
        }
    }
}
