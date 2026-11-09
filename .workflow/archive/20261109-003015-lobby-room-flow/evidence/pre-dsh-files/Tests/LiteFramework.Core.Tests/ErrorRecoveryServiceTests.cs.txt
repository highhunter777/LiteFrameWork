using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using LiteFramework;
using Xunit;

namespace LiteFramework.Core.Tests
{
    /// <summary>
    /// M3-5b 错误恢复执行器（《客户端依赖注入专项设计》§6.2）：分类 → 动作 → 执行。
    /// 纯逻辑，L1 直测（退避与动作端口用替身，不 sleep、不落盘）。
    /// </summary>
    public sealed class ErrorRecoveryServiceTests
    {
        private sealed class FakeActions : IRecoveryActions
        {
            public int ExportCount;
            public int ExitCount;
            public int ClearCount;
            public string LastReason;

            public UniTask<string> ExportDiagnosticsAsync(string reason, CancellationToken ct)
            {
                ExportCount++;
                LastReason = reason;
                return UniTask.FromResult("recovery/report.txt");
            }

            public UniTask ExitAsync(CancellationToken ct)
            {
                ExitCount++;
                return UniTask.CompletedTask;
            }

            public UniTask<bool> ClearContentCacheAsync(CancellationToken ct)
            {
                ClearCount++;
                return UniTask.FromResult(true);
            }
        }

        private static (ErrorRecoveryService service, FakeActions actions, List<TimeSpan> delays) Build(
            RecoveryPolicy policy = null)
        {
            var actions = new FakeActions();
            var delays = new List<TimeSpan>();
            var service = new ErrorRecoveryService(
                actions,
                policy ?? new RecoveryPolicy(maxAttempts: 3, initialDelay: TimeSpan.FromMilliseconds(10),
                    maxDelay: TimeSpan.FromMilliseconds(25), backoffFactor: 2.0),
                (delay, ct) => { delays.Add(delay); return UniTask.CompletedTask; });
            return (service, actions, delays);
        }

        [Fact]
        public void 分类_标记透传_未标记Fatal()
        {
            Assert.Equal(ErrorKind.Security, ErrorRecoveryService.Classify(
                new ClientErrorException(ErrorKind.Security, "票据篡改")));
            Assert.Equal(ErrorKind.Fatal, ErrorRecoveryService.Classify(new InvalidOperationException("未知")));
            Assert.Equal(ErrorKind.Fatal, ErrorRecoveryService.Classify(null));
            Assert.True(ErrorClassification.IsCancellation(new OperationCanceledException()));
            Assert.False(ErrorClassification.IsCancellation(new InvalidOperationException()));
        }

        [Theory]
        [InlineData(ErrorKind.Transient, RecoveryAction.Retry)]
        [InlineData(ErrorKind.Recoverable, RecoveryAction.Retry)]
        [InlineData(ErrorKind.Compatibility, RecoveryAction.Block)]
        [InlineData(ErrorKind.Security, RecoveryAction.ExportAndBlock)]
        [InlineData(ErrorKind.Fatal, RecoveryAction.ExportAndExit)]
        public void 动作映射_五分类(ErrorKind kind, RecoveryAction expected)
        {
            Assert.Equal(expected, ErrorRecoveryService.Resolve(kind));
        }

        [Fact]
        public async Task 重试_第二次成功_按策略退避()
        {
            (ErrorRecoveryService service, FakeActions actions, List<TimeSpan> delays) = Build();
            int calls = 0;

            RecoveryOutcome outcome = await service.HandleAsync(
                new ClientErrorException(ErrorKind.Transient, "网络抖动"), "content.patch",
                ct => UniTask.FromResult(++calls == 2));

            Assert.True(outcome.Recovered);
            Assert.Equal(2, outcome.Attempts);
            Assert.Equal(2, calls);
            Assert.Single(delays);
            Assert.Equal(TimeSpan.FromMilliseconds(10), delays[0]);
            Assert.Equal(0, actions.ExportCount);
        }

        [Fact]
        public async Task 重试_耗尽_三次后阻断()
        {
            (ErrorRecoveryService service, _, List<TimeSpan> delays) = Build();
            int calls = 0;

            RecoveryOutcome outcome = await service.HandleAsync(
                new ClientErrorException(ErrorKind.Recoverable, "缓存损坏"), "content.patch",
                ct => { calls++; return UniTask.FromResult(false); });

            Assert.Equal(RecoveryResult.Exhausted, outcome.Result);
            Assert.True(outcome.Blocked);
            Assert.Equal(3, outcome.Attempts);
            Assert.Equal(3, calls);
            Assert.Equal(2, delays.Count);                        // 末次失败后不再等待
            Assert.Equal(TimeSpan.FromMilliseconds(10), delays[0]);
            Assert.Equal(TimeSpan.FromMilliseconds(20), delays[1]);
        }

        [Fact]
        public async Task 可恢复_重试耗尽_清受控缓存一次_再阻断()
        {
            (ErrorRecoveryService service, FakeActions actions, _) = Build();

            RecoveryOutcome outcome = await service.HandleAsync(
                new ClientErrorException(ErrorKind.Recoverable, "缓存损坏"), "content.patch",
                ct => UniTask.FromResult(false));

            Assert.Equal(RecoveryResult.Exhausted, outcome.Result);
            Assert.Equal(1, actions.ClearCount);                   // 升级动作只做一次（不随重试次数放大）
            Assert.Contains("已清理受控内容缓存", outcome.Message);
        }

        [Fact]
        public async Task 可恢复_重试成功_不清缓存()
        {
            (ErrorRecoveryService service, FakeActions actions, _) = Build();

            RecoveryOutcome outcome = await service.HandleAsync(
                new ClientErrorException(ErrorKind.Recoverable, "旧清单"), "content.patch",
                ct => UniTask.FromResult(true));

            Assert.True(outcome.Recovered);
            Assert.Equal(0, actions.ClearCount);
        }

        [Fact]
        public async Task 可恢复_无重试委托_清受控缓存后阻断()
        {
            (ErrorRecoveryService service, FakeActions actions, _) = Build();

            RecoveryOutcome outcome = await service.HandleAsync(
                new ClientErrorException(ErrorKind.Recoverable, "缓存损坏"), "bootstrap.error");

            Assert.Equal(RecoveryResult.Blocked, outcome.Result);
            Assert.Equal(1, actions.ClearCount);
            Assert.Contains("已清理受控内容缓存", outcome.Message);
        }

        [Fact]
        public async Task 重试_委托抛异常_记为失败并继续()
        {
            (ErrorRecoveryService service, _, _) = Build();
            int calls = 0;

            RecoveryOutcome outcome = await service.HandleAsync(
                new ClientErrorException(ErrorKind.Transient, "网络抖动"), "content.patch",
                ct =>
                {
                    calls++;
                    if (calls < 3) throw new InvalidOperationException("再次失败");
                    return UniTask.FromResult(true);
                });

            Assert.True(outcome.Recovered);
            Assert.Equal(3, outcome.Attempts);
            Assert.Equal(3, calls);
        }

        [Fact]
        public async Task 暂态但无重试委托_阻断()
        {
            (ErrorRecoveryService service, _, _) = Build();

            RecoveryOutcome outcome = await service.HandleAsync(
                new ClientErrorException(ErrorKind.Transient, "网络抖动"), "session.connect");

            Assert.Equal(RecoveryResult.Blocked, outcome.Result);
            Assert.Equal(0, outcome.Attempts);
        }

        [Fact]
        public async Task 暂态_不清缓存_网络抖动与本地缓存无关()
        {
            (ErrorRecoveryService service, FakeActions actions, _) = Build();

            await service.HandleAsync(new ClientErrorException(ErrorKind.Transient, "超时"), "content.patch",
                ct => UniTask.FromResult(false));

            Assert.Equal(0, actions.ClearCount);
        }

        [Fact]
        public async Task 兼容性_阻断_无副作用()
        {
            (ErrorRecoveryService service, FakeActions actions, _) = Build();

            RecoveryOutcome outcome = await service.HandleAsync(
                new ClientErrorException(ErrorKind.Compatibility, "版本不符"), "content.patch");

            Assert.Equal(RecoveryResult.Blocked, outcome.Result);
            Assert.Equal(0, actions.ExportCount);
            Assert.Equal(0, actions.ExitCount);
        }

        [Fact]
        public async Task 安全_导出诊断后阻断_不退出()
        {
            (ErrorRecoveryService service, FakeActions actions, _) = Build();

            RecoveryOutcome outcome = await service.HandleAsync(
                new ClientErrorException(ErrorKind.Security, "签名失败"), "content.patch");

            Assert.Equal(RecoveryResult.Blocked, outcome.Result);
            Assert.Equal(1, actions.ExportCount);
            Assert.Equal(0, actions.ExitCount);
            Assert.Contains("content.patch", actions.LastReason);
            Assert.Contains("Security", actions.LastReason);
        }

        [Fact]
        public async Task 致命_导出诊断并退出()
        {
            (ErrorRecoveryService service, FakeActions actions, _) = Build();

            RecoveryOutcome outcome = await service.HandleAsync(new InvalidOperationException("初始化不变量破坏"), "bootstrap");

            Assert.Equal(RecoveryResult.Fatal, outcome.Result);
            Assert.Equal(1, actions.ExportCount);
            Assert.Equal(1, actions.ExitCount);
        }

        [Fact]
        public async Task 取消_不是错误_不触发动作()
        {
            (ErrorRecoveryService service, FakeActions actions, _) = Build();

            RecoveryOutcome outcome = await service.HandleAsync(new OperationCanceledException(), "ui.open");

            Assert.Equal(RecoveryResult.Canceled, outcome.Result);
            Assert.Equal(0, actions.ExportCount);
            Assert.Equal(0, actions.ExitCount);
        }

        [Fact]
        public void 策略_非法参数抛_退避封顶()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new RecoveryPolicy(maxAttempts: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RecoveryPolicy(backoffFactor: 0.5));
            var policy = new RecoveryPolicy(3, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(250), 2.0);
            Assert.Equal(TimeSpan.FromMilliseconds(100), policy.DelayFor(1));
            Assert.Equal(TimeSpan.FromMilliseconds(200), policy.DelayFor(2));
            Assert.Equal(TimeSpan.FromMilliseconds(250), policy.DelayFor(3));
        }
    }
}
