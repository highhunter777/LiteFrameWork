using System;
using LiteFramework;
using Xunit;

namespace LiteFramework.Tests
{
    // SafeCall（设计方案 §4.3 事件桥前置件）：调用即防护——单回调抛异常不外泄、派发链存活
    [Collection("CoreStatic")]   // 异常路径写 Log.Error（全局静态，无线程锁）；本组直接断言 Log.ErrorCount 增量
    public sealed class SafeCallTests
    {
        [Fact]
        public void Invoke_Action_正常执行()
        {
            var called = false;
            SafeCall.Invoke(() => called = true, "test");
            Assert.True(called);
        }

        [Fact]
        public void Invoke_Action_抛异常被隔离不外泄()
        {
            var before = Log.ErrorCount;                    // 静态累计值：断言增量而非绝对值（用例间共享）
            SafeCall.Invoke(() => throw new InvalidOperationException("boom"), "test");
            Assert.Equal(before + 1, Log.ErrorCount);       // 异常落地 C# 日志（验收线 6）
        }

        [Fact]
        public void Invoke_Func_返回值透传()
        {
            var result = SafeCall.Invoke(() => 42, "test");
            Assert.Equal(42, result);
        }

        [Fact]
        public void Invoke_Func_抛异常返回fallback()
        {
            var result = SafeCall.Invoke<int>(() => throw new InvalidOperationException("boom"), "test", -1);
            Assert.Equal(-1, result);
        }

        [Fact]
        public void TryInvoke_正常执行返回true()
        {
            var called = false;
            Assert.True(SafeCall.TryInvoke(() => called = true, "test"));
            Assert.True(called);
        }

        [Fact]
        public void TryInvoke_抛异常被隔离并返回false()
        {
            var before = Log.ErrorCount;
            Assert.False(SafeCall.TryInvoke(() => throw new InvalidOperationException("boom"), "test"));
            Assert.Equal(before + 1, Log.ErrorCount);       // 异常落地 C# 日志（调用方可据此回滚）
        }
    }
}
