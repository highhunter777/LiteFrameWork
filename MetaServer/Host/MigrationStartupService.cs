using System;
using System.Threading;
using System.Threading.Tasks;
using MetaServer.Contracts.Persistence;
using MetaServer.Infrastructure.Persistence.Mongo;
using Microsoft.Extensions.Hosting;

namespace MetaServer
{
    /// <summary>
    /// 启动迁移（M0-c 批二；Meta 专项 §9.1"迁移失败有明确结果与回滚方式，不静默半迁移"）。
    /// StartAsync 里迁移失败（Failed/Rejected）即抛异常——宿主拒绝启动，fail-closed；
    /// 已在目标版本/完成则放行。Stop 无收尾动作——迁移只在启动发生一次。
    /// </summary>
    internal sealed class MigrationStartupService : IHostedService
    {
        private readonly ISchemaMigrator _migrator;

        public MigrationStartupService(ISchemaMigrator migrator)
        {
            _migrator = migrator ?? throw new ArgumentNullException(nameof(migrator));
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            MigrationOutcome outcome;
            try
            {
                outcome = await _migrator.MigrateAsync(MongoMigrations.LatestVersion, cancellationToken);
            }
            catch (Exception ex)
            {
                // 存储异常（如服务选择超时）同样拒绝启动——统一为稳定异常类型
                throw new InvalidOperationException("启动迁移失败（存储异常）：" + ex.Message, ex);
            }

            switch (outcome)
            {
                case MigrationOutcome.Completed _:
                case MigrationOutcome.UpToDate _:
                    return;
                case MigrationOutcome.Failed failed:
                    throw new InvalidOperationException(
                        "启动迁移失败：版本 " + failed.FailedAtVersion + "（" + failed.Reason
                        + "）；已回滚至 " + failed.RecoveredVersion
                        + "，回滚失败=" + failed.RollbackFailed);
                case MigrationOutcome.Rejected rejected:
                    throw new InvalidOperationException("启动迁移被拒绝：" + rejected.Reason);
                default:
                    throw new InvalidOperationException("启动迁移返回未知结果形态：" + outcome.GetType().Name);
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
