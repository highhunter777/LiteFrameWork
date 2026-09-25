using System;
using System.Collections.Generic;
using Xunit;

namespace LiteFramework.Tests
{
    /// <summary>
    /// 补丁编排（《热更与内容发布专项设计》§8 激活、健康确认与中断恢复；§4 流程图）。
    ///
    /// 本类是热更链的**第一个生产消费者**——把此前无调用方的描述校验、下载计划、
    /// 候选校验、激活事务串成一条可测编排。
    /// </summary>
    public sealed class PatchCoordinatorTests
    {
        private static (PatchCoordinator coord, FakeActivationIO io, FakeCandidateFileSource files,
            FakeDiskSpaceProbe disk, FakeFetcher fetcher, FakeHealthCheck health, FakeActivator activator,
            FakeGenerationSink sink, ActivationTransactionStore store)
            Build(string initialReleaseId = "builtin", ulong initialVersion = 0,
                  long availableBytes = long.MaxValue, string unhealthyReason = null)
        {
            var io = new FakeActivationIO();
            if (initialReleaseId != "builtin" || initialVersion != 0)
                io.Seed(new ActivationRecord { ConfirmedReleaseId = initialReleaseId, ConfirmedVersion = initialVersion });

            var store = new ActivationTransactionStore(io, () => 12345);
            var files = new FakeCandidateFileSource();
            var disk = new FakeDiskSpaceProbe(availableBytes);
            var fetcher = new FakeFetcher();
            var health = new FakeHealthCheck { UnhealthyReason = unhealthyReason };
            var activator = new FakeActivator();
            var sink = new FakeGenerationSink();

            var coord = new PatchCoordinator(store, files, disk, fetcher, health, activator, sink);
            return (coord, io, files, disk, fetcher, health, activator, sink, store);
        }

        [Fact]
        public void 正常路径_全链路推进到Confirmed()
        {
            var (coord, io, files, _, _, health, activator, sink, _) = Build();
            ReleaseManifest m = PatchTestFixtures.ManifestWithFiles(files, "rel-2", ("a.bin", "AAA"), ("b.bin", "BB"));

            PatchRunResult r = PatchTestFixtures.Pump(
                coord.RunAsync(m, PatchTestFixtures.PlanFor(m), PatchTestFixtures.SpaceFor(m)));

            Assert.True(r.Succeeded, r.ToString());
            Assert.Equal(PatchPhase.Confirmed, r.FinalPhase);
            Assert.Equal(1, activator.ActivateCalls);
            Assert.Equal(1, health.Calls);
            Assert.Equal("rel-2", activator.LastActivatedReleaseId);

            // 代次推进到新确认版本（否则新内容永远不会被加载）
            Assert.Single(sink.Advised);
            Assert.Equal("rel-2", sink.Advised[0].ReleaseId);
            Assert.Equal(1UL, sink.Advised[0].Value);

            // 每一步都持久化：BeginCandidate + MarkPendingActivation + Confirm ≥ 3 次
            Assert.True(io.SaveCount >= 3, $"写盘次数 {io.SaveCount}");
        }

        [Fact]
        public void 无候选_不进入激活链路()
        {
            var (coord, _, _, _, fetcher, health, activator, _, _) = Build();

            PatchRunResult r = PatchTestFixtures.Pump(
                coord.RunAsync(null, null, new SpaceCheckRequest()));

            Assert.True(r.NoWork);
            Assert.False(r.Succeeded);
            Assert.Equal(0, fetcher.Calls);
            Assert.Equal(0, health.Calls);
            Assert.Equal(0, activator.ActivateCalls);
        }

        [Fact]
        public void 空间不足_拒绝且不获取不激活()
        {
            var (coord, io, files, _, fetcher, _, activator, _, store) = Build(availableBytes: 1);
            ReleaseManifest m = PatchTestFixtures.ManifestWithFiles(files, "rel-2", ("a.bin", "AAAA"));

            PatchRunResult r = PatchTestFixtures.Pump(
                coord.RunAsync(m, PatchTestFixtures.PlanFor(m), PatchTestFixtures.SpaceFor(m)));

            Assert.False(r.Succeeded);
            Assert.False(r.NoWork);
            Assert.Equal(PatchPhase.PrecheckingSpace, r.FinalPhase);   // 失败发生在预检阶段
            Assert.Equal(PatchPhase.Failed, coord.Phase);              // 编排整体进入失败态
            Assert.Equal(DownloadFailureKind.InsufficientSpace, r.Failure.Kind);
            Assert.Equal(0, fetcher.Calls);
            Assert.Equal(0, activator.ActivateCalls);
            Assert.Contains("InsufficientSpace", store.Current.LastFailure);
            Assert.Null(store.Current.PendingReleaseId);               // 预检失败不开候选事务（无临时归属可清）
        }

        [Fact]
        public void 获取失败_候选事务先于下载落盘_临时文件随即回收()
        {
            var (coord, _, files, _, fetcher, _, _, _, store) = Build(initialReleaseId: "rel-1", initialVersion: 5);
            ReleaseManifest m = PatchTestFixtures.ManifestWithFiles(files, "rel-2", ("a.bin", "AAA"));
            fetcher.Succeed = false;
            fetcher.FailKind = DownloadFailureKind.TransientNetwork;
            string pendingAtFetchStart = "<unset>";
            fetcher.BeforeFetch = () => pendingAtFetchStart = store.Current.PendingReleaseId;

            PatchRunResult r = PatchTestFixtures.Pump(
                coord.RunAsync(m, PatchTestFixtures.PlanFor(m), PatchTestFixtures.SpaceFor(m)));

            Assert.False(r.Succeeded);
            Assert.Equal(PatchPhase.Fetching, r.FinalPhase);
            // §8 表行 1：Candidate 事务在下载开始前已落盘——下载/校验中中断时记录持有临时归属
            Assert.Equal("rel-2", pendingAtFetchStart);
            Assert.Equal("rel-2", store.Current.PendingReleaseId);
            Assert.Equal(ActivationState.Candidate, store.Current.PendingState);
            // 失败收尾即回收该候选的临时文件（不等下次启动）；已确认版本保留不动
            Assert.Contains("rel-2", fetcher.Cleaned);
            Assert.Equal("rel-1", store.Current.ConfirmedReleaseId);
            Assert.Equal(5UL, store.Current.ConfirmedVersion);
        }

        [Fact]
        public void 无候选_启动恢复仍回收上次在途候选的临时文件()
        {
            var (coord, _, _, _, fetcher, health, activator, _, store) = Build(initialReleaseId: "rel-1", initialVersion: 7);
            store.BeginCandidate("rel-dead");                          // 模拟上次下载/校验中被杀：记录在途

            PatchRunResult r = PatchTestFixtures.Pump(
                coord.RunAsync(null, null, new SpaceCheckRequest()));

            Assert.True(r.NoWork);
            Assert.Contains("rel-dead", fetcher.Cleaned);              // §8 表行 1：恢复即清理临时归属
            Assert.Equal(0, health.Calls);
            Assert.Equal(0, activator.ActivateCalls);
            Assert.Null(store.Current.PendingReleaseId);               // 在途事务已了结
        }

        [Fact]
        public void 获取失败_保留已确认版本()
        {
            var (coord, _, files, _, fetcher, health, activator, _, store) = Build(initialReleaseId: "rel-1", initialVersion: 5);
            ReleaseManifest m = PatchTestFixtures.ManifestWithFiles(files, "rel-2", ("a.bin", "AAA"));
            fetcher.Succeed = false;
            fetcher.FailKind = DownloadFailureKind.TransientNetwork;

            PatchRunResult r = PatchTestFixtures.Pump(
                coord.RunAsync(m, PatchTestFixtures.PlanFor(m), PatchTestFixtures.SpaceFor(m)));

            Assert.False(r.Succeeded);
            Assert.Equal(PatchPhase.Fetching, r.FinalPhase);
            Assert.Equal(0, health.Calls);
            Assert.Equal(0, activator.ActivateCalls);
            Assert.Equal("rel-1", store.Current.ConfirmedReleaseId);      // 已确认版本未被改动
            Assert.Equal(5UL, store.Current.ConfirmedVersion);
        }

        [Fact]
        public void 校验失败_内容损坏_不激活()
        {
            var (coord, _, files, _, _, health, activator, _, store) = Build();
            ReleaseManifest m = PatchTestFixtures.ManifestWithFiles(files, "rel-2", ("a.bin", "AAA"));

            // 篡改落盘内容（保持长度），摘要复算应检出
            files.Add("a.bin", System.Text.Encoding.UTF8.GetBytes("XXX"));

            PatchRunResult r = PatchTestFixtures.Pump(
                coord.RunAsync(m, PatchTestFixtures.PlanFor(m), PatchTestFixtures.SpaceFor(m)));

            Assert.False(r.Succeeded);
            Assert.Equal(PatchPhase.Verifying, r.FinalPhase);
            Assert.Equal(DownloadFailureKind.DigestMismatch, r.Failure.Kind);
            Assert.Equal(0, activator.ActivateCalls);                     // 不发布半成品
            Assert.Contains("Verifying", store.Current.LastFailure);
        }

        [Fact]
        public void 健康失败_回退已确认版本()
        {
            var (coord, _, files, _, _, _, activator, sink, store) = Build(
                initialReleaseId: "rel-1", initialVersion: 3, unhealthyReason: "候选 ConfigSnapshot 校验失败");
            ReleaseManifest m = PatchTestFixtures.ManifestWithFiles(files, "rel-2", ("a.bin", "AAA"));

            PatchRunResult r = PatchTestFixtures.Pump(
                coord.RunAsync(m, PatchTestFixtures.PlanFor(m), PatchTestFixtures.SpaceFor(m)));

            Assert.False(r.Succeeded);
            Assert.Equal(PatchPhase.HealthChecking, r.FinalPhase);
            Assert.Equal(1, activator.ActivateCalls);
            Assert.Equal(1, activator.RebuildCalls);                      // §8 从 Confirmed 重建
            Assert.Equal("rel-1", activator.LastRebuiltGeneration.ReleaseId);
            Assert.Equal(3UL, activator.LastRebuiltGeneration.Value);

            // 代次先推进后回退：最终提示的是已确认版本
            Assert.Equal("rel-1", sink.Advised[sink.Advised.Count - 1].ReleaseId);
        }

        [Fact]
        public void 健康失败且重建抛错_仍返回失败阶段而非掩盖()
        {
            var (coord, _, files, _, _, _, activator, _, store) = Build(
                initialReleaseId: "rel-1", initialVersion: 3, unhealthyReason: "坏");
            ReleaseManifest m = PatchTestFixtures.ManifestWithFiles(files, "rel-2", ("a.bin", "AAA"));
            activator.RebuildThrow = new InvalidOperationException("重建炸了");

            PatchRunResult r = PatchTestFixtures.Pump(
                coord.RunAsync(m, PatchTestFixtures.PlanFor(m), PatchTestFixtures.SpaceFor(m)));

            Assert.False(r.Succeeded);
            Assert.Equal(PatchPhase.HealthChecking, r.FinalPhase);        // 真正的失败阶段没被掩盖
            Assert.Contains("重建失败", store.Current.LastFailure);
        }

        [Fact]
        public void 依赖未确认_拒绝()
        {
            var (coord, _, files, _, _, _, activator, _, _) = Build(initialReleaseId: "rel-0");
            ReleaseManifest m = PatchTestFixtures.ManifestWithFiles(files, "rel-2", ("a.bin", "AAA"));
            m.Dependencies = new List<string> { "rel-dep-missing" };

            PatchRunResult r = PatchTestFixtures.Pump(
                coord.RunAsync(m, PatchTestFixtures.PlanFor(m), PatchTestFixtures.SpaceFor(m)));

            Assert.False(r.Succeeded);
            Assert.Equal(PatchPhase.Recovering, r.FinalPhase);
            Assert.Equal(0, activator.ActivateCalls);
        }

        [Fact]
        public void 依赖已确认_放行()
        {
            var (coord, _, files, _, _, _, _, _, _) = Build(initialReleaseId: "rel-dep");
            ReleaseManifest m = PatchTestFixtures.ManifestWithFiles(files, "rel-2", ("a.bin", "AAA"));
            m.Dependencies = new List<string> { "rel-dep" };

            PatchRunResult r = PatchTestFixtures.Pump(
                coord.RunAsync(m, PatchTestFixtures.PlanFor(m), PatchTestFixtures.SpaceFor(m)));

            Assert.True(r.Succeeded, r.ToString());
        }

        [Fact]
        public void 中断恢复_在途候选不回退已确认且以Confirmed继续()
        {
            // 模拟"上次在途候选被杀"：记录里有在途候选
            var (coord, _, files, _, fetcher, _, activator, _, store) = Build(initialReleaseId: "rel-1", initialVersion: 7);
            store.BeginCandidate("rel-dead");
            store.MarkPendingActivation();
            store.RecordFailure("上次激活中断");

            ReleaseManifest m = PatchTestFixtures.ManifestWithFiles(files, "rel-2", ("a.bin", "AAA"));
            PatchRunResult r = PatchTestFixtures.Pump(
                coord.RunAsync(m, PatchTestFixtures.PlanFor(m), PatchTestFixtures.SpaceFor(m)));

            // 恢复决策先跑（在途被放弃、以 Confirmed 继续），随后新候选正常走完
            Assert.True(r.Succeeded, r.ToString());
            Assert.Equal("rel-2", store.Current.ConfirmedReleaseId);
            Assert.Equal(1, activator.ActivateCalls);
            Assert.Contains("rel-dead", fetcher.Cleaned);              // §8 表行 1：恢复时回收在途候选的临时归属
        }

        [Fact]
        public void 阶段推进_可观测()
        {
            var (coord, _, files, _, _, _, _, _, _) = Build();
            ReleaseManifest m = PatchTestFixtures.ManifestWithFiles(files, "rel-2", ("a.bin", "AAA"));

            Assert.Equal(PatchPhase.Idle, coord.Phase);                  // 未开始
            PatchTestFixtures.Pump(coord.RunAsync(m, PatchTestFixtures.PlanFor(m), PatchTestFixtures.SpaceFor(m)));
            Assert.Equal(PatchPhase.Confirmed, coord.Phase);
        }

        [Fact]
        public void 无代次接收方_不影响编排()
        {
            var io = new FakeActivationIO();
            var store = new ActivationTransactionStore(io, () => 1);
            var files = new FakeCandidateFileSource();
            var coord = new PatchCoordinator(store, files, new FakeDiskSpaceProbe(long.MaxValue),
                new FakeFetcher(), new FakeHealthCheck(), new FakeActivator(), generationSink: null);

            ReleaseManifest m = PatchTestFixtures.ManifestWithFiles(files, "rel-2", ("a.bin", "A"));
            PatchRunResult r = PatchTestFixtures.Pump(
                coord.RunAsync(m, PatchTestFixtures.PlanFor(m), PatchTestFixtures.SpaceFor(m)));

            Assert.True(r.Succeeded, r.ToString());
        }

        [Fact]
        public void 清单外文件_校验拒绝_不激活()
        {
            var (coord, _, files, _, fetcher, _, activator, _, _) = Build();
            ReleaseManifest m = PatchTestFixtures.ManifestWithFiles(files, "rel-2", ("a.bin", "AAA"));
            fetcher.PathsToReturn = new List<string> { "a.bin", "sneaked.bin" };   // 获取结果多出未声明文件

            PatchRunResult r = PatchTestFixtures.Pump(
                coord.RunAsync(m, PatchTestFixtures.PlanFor(m), PatchTestFixtures.SpaceFor(m)));

            Assert.False(r.Succeeded);
            Assert.Equal(DownloadFailureKind.UnexpectedFile, r.Failure.Kind);
            Assert.Equal(0, activator.ActivateCalls);
        }
    }

    /// <summary>
    /// 健康确认聚合（《热更与内容发布专项设计》§8"健康确认至少覆盖候选 ConfigSnapshot、Lua/main、
    /// 全部必需注册表、关键 UI/入口及其资源"）。
    /// </summary>
    public sealed class CompositeHealthCheckTests
    {
        private static ReleaseManifest AnyManifest()
            => new ReleaseManifest { ReleaseId = "rel", Files = new List<ReleaseFileEntry> { new ReleaseFileEntry { Path = "a" } } };

        [Fact]
        public void 全部健康_返回null()
        {
            var check = new CompositeHealthCheck(
                new FakeHealthProbe("config"), new FakeHealthProbe("lua-main"), new FakeHealthProbe("registry"));

            Assert.Null(PatchTestFixtures.Pump(check.CheckAsync(AnyManifest())));
        }

        [Fact]
        public void 无探针_判为不健康()
        {
            // 未确认任何覆盖项就声称健康，等于放弃 §8 的覆盖要求
            var check = new CompositeHealthCheck();
            string reason = PatchTestFixtures.Pump(check.CheckAsync(AnyManifest()));

            Assert.NotNull(reason);
            Assert.Contains("无健康探针", reason);
        }

        [Fact]
        public void 一次报告全部失败项_不短路()
        {
            var a = new FakeHealthProbe("config", "坏表");
            var b = new FakeHealthProbe("lua-main", "语法错误");
            var c = new FakeHealthProbe("registry");
            var check = new CompositeHealthCheck(a, b, c);

            string reason = PatchTestFixtures.Pump(check.CheckAsync(AnyManifest()));

            Assert.Contains("坏表", reason);
            Assert.Contains("语法错误", reason);
            Assert.Equal(1, c.Calls);          // 健康的探针也被执行（不短路）
        }

        [Fact]
        public void 探针抛异常_按不健康处理且不中断链路()
        {
            var bad = new FakeHealthProbe("config") { Throw = new InvalidOperationException("探针炸了") };
            var ok = new FakeHealthProbe("lua-main");
            var check = new CompositeHealthCheck(bad, ok);

            string reason = PatchTestFixtures.Pump(check.CheckAsync(AnyManifest()));

            Assert.Contains("探针异常", reason);
            Assert.Equal(1, ok.Calls);         // 后续探针仍执行——异常不被当作健康
        }

        [Fact]
        public void 取消_返回取消说明()
        {
            var check = new CompositeHealthCheck(new FakeHealthProbe("config"));
            using var cts = new System.Threading.CancellationTokenSource();
            cts.Cancel();

            string reason = PatchTestFixtures.Pump(check.CheckAsync(AnyManifest(), cts.Token));
            Assert.Contains("取消", reason);
        }

        [Fact]
        public void Add与计数()
        {
            var check = new CompositeHealthCheck();
            Assert.Equal(0, check.ProbeCount);
            check.Add(new FakeHealthProbe("a")).Add(new FakeHealthProbe("b")).Add(null);
            Assert.Equal(2, check.ProbeCount);
        }
    }
}
