using System;
using LiteFramework;
using Xunit;

namespace LiteFramework.Tests
{
    /// <summary>
    /// 错误恢复骨架用例（《商业级通用客户端框架总设计》§7.2 错误分类表逐行映射 + 取消 ≠ 错误）。
    /// </summary>
    public sealed class ClientErrorRecoveryTests
    {
        [Theory]
        [InlineData(ClientErrorKind.Transient, RecoveryAction.Retry)]
        [InlineData(ClientErrorKind.Recoverable, RecoveryAction.ClearCacheAndRetry)]
        [InlineData(ClientErrorKind.Compatibility, RecoveryAction.ExportDiagnosticsAndExit)]
        [InlineData(ClientErrorKind.Security, RecoveryAction.ExportDiagnosticsAndExit)]
        [InlineData(ClientErrorKind.Fatal, RecoveryAction.ExportDiagnosticsAndExit)]
        public void 分类到动作_按设计分类表映射(ClientErrorKind kind, RecoveryAction expected)
        {
            Assert.Equal(expected, ClientErrorRecovery.Resolve(kind));
        }

        [Fact]
        public void 分类_未标记未知异常_按Fatal兜底_不重试()
        {
            Assert.Equal(ClientErrorKind.Fatal, ClientErrorRecovery.Classify(new InvalidOperationException("未知")));
            Assert.Equal(RecoveryAction.ExportDiagnosticsAndExit,
                ClientErrorRecovery.ResolveUnknown(new InvalidOperationException("未知")));
        }

        [Fact]
        public void 分类_取消异常按Transient_不触发退出()
        {
            var oce = new OperationCanceledException();
            Assert.Equal(ClientErrorKind.Transient, ClientErrorRecovery.Classify(oce));
            Assert.Equal(RecoveryAction.Retry, ClientErrorRecovery.ResolveUnknown(oce));
        }

        [Fact]
        public void 分类_标记异常_透传其分类()
        {
            var marked = new ClientRecoveryException(ClientErrorKind.Security, "票据篡改");
            Assert.Equal(ClientErrorKind.Security, ClientErrorRecovery.Classify(marked));
            Assert.Equal(RecoveryAction.ExportDiagnosticsAndExit, ClientErrorRecovery.ResolveUnknown(marked));
        }

        [Fact]
        public void 分类_null异常_按Fatal兜底()
        {
            Assert.Equal(ClientErrorKind.Fatal, ClientErrorRecovery.Classify(null));
        }
    }
}
