using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MetaServer.Contracts.Persistence
{
    /// <summary>
    /// 一次显式版本化的迁移步骤（Meta 专项 §9.1"索引与迁移显式版本化"）。
    ///
    /// 契约要求：
    /// - <see cref="RollbackAsync"/> 与 <see cref="MigrateAsync"/> 必须互为逆操作——§9.1
    ///   "迁移失败有明确结果与回滚方式"要求回滚是真实能力，不是注释里的愿望；
    /// - <see cref="MigrateAsync"/> 必须可重复应用——版本确认点（<see cref="ISchemaVersionStore"/>
    ///   写回）失败时执行器会回滚本步，进程终止后重启会从最后确认版本重新执行该步。
    /// </summary>
    public interface IMigrationStep
    {
        long Version { get; }

        string Description { get; }

        ValueTask MigrateAsync(CancellationToken ct);

        ValueTask RollbackAsync(CancellationToken ct);
    }

    /// <summary>
    /// 当前 schema 版本的持久读写。**版本写回是迁移的确认点**——每步成功后写回；
    /// 进程在任意一步后终止，重启都从最后确认的版本继续，不重复应用已确认步骤。
    /// </summary>
    public interface ISchemaVersionStore
    {
        ValueTask<long> ReadCurrentAsync(CancellationToken ct);

        ValueTask WriteAsync(long version, CancellationToken ct);
    }

    /// <summary>
    /// 迁移结果四态——失败必须显式报告"失败在哪一步、回到哪个版本"，不得静默半迁移（§9.1）。
    /// </summary>
    public abstract record MigrationOutcome
    {
        /// <summary>已在目标版本（含空步骤表），未做任何事。</summary>
        public sealed record UpToDate(long CurrentVersion) : MigrationOutcome;

        /// <summary>本次按序应用的版本序列（每步成功即已写回版本存储）。</summary>
        public sealed record Completed(IReadOnlyList<long> AppliedVersions) : MigrationOutcome;

        /// <summary>
        /// 失败：<see cref="FailedAtVersion"/> 一步抛错；已按逆序回滚（含失败步本身）；
        /// 回滚自身失败时 <see cref="RollbackFailed"/> 为真，<see cref="RecoveredVersion"/>
        /// 仅为尽力报告值——宿主必须以失败对待，不得继续启动。
        /// </summary>
        public sealed record Failed(
            long FailedAtVersion, string Reason, long RecoveredVersion, bool RollbackFailed)
            : MigrationOutcome;

        /// <summary>
        /// 步骤表不连续（版本缺口/重复）、目标低于当前版本或超出步骤表覆盖——
        /// 显式拒绝，不静默跳过、不静默降级。
        /// </summary>
        public sealed record Rejected(string Reason) : MigrationOutcome;
    }

    /// <summary>
    /// 迁移执行端口。宿主启动时调用（真实 Mongo 适配批接线），失败即拒绝启动，
    /// 不得带半迁移状态继续运行（§9.1"不静默半迁移"）。
    /// </summary>
    public interface ISchemaMigrator
    {
        ValueTask<MigrationOutcome> MigrateAsync(long targetVersion, CancellationToken ct);
    }
}
