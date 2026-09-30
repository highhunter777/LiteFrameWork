using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MetaServer.Contracts.Persistence;

namespace MetaServer.Infrastructure.Persistence
{
    /// <summary>
    /// 顺序迁移执行器（Meta 专项 §9.1）：步骤表连续性校验 → 从当前版本逐版本应用（每步成功即写回
    /// 版本存储＝确认点）→ 失败即逆序回滚（含失败步本身）并显式报告——"迁移失败有明确结果与
    /// 回滚方式，不静默半迁移"。
    ///
    /// 无墙钟/无 IO——所有持久动作经 <see cref="ISchemaVersionStore"/> 与
    /// <see cref="IMigrationStep"/> 端口注入，本类保持 R11 纯净（真实 Mongo 步骤在驱动适配批落
    /// Infrastructure 时另行处理扫描豁免裁决）。
    /// </summary>
    public sealed class SchemaMigrationRunner : ISchemaMigrator
    {
        private readonly IReadOnlyList<IMigrationStep> _steps;
        private readonly ISchemaVersionStore _versionStore;

        public SchemaMigrationRunner(IReadOnlyList<IMigrationStep> steps, ISchemaVersionStore versionStore)
        {
            _steps = steps ?? throw new ArgumentNullException(nameof(steps));
            _versionStore = versionStore ?? throw new ArgumentNullException(nameof(versionStore));
        }

        public async ValueTask<MigrationOutcome> MigrateAsync(long targetVersion, CancellationToken ct)
        {
            // 步骤表连续性：版本必须从 1 起无缺口、无重复——缺口等于"静默跳过某版本"，重复等于重复执行
            for (int i = 0; i < _steps.Count; i++)
            {
                if (_steps[i].Version != i + 1)
                {
                    return new MigrationOutcome.Rejected(
                        "步骤表不连续：第 " + (i + 1) + " 项应为版本 " + (i + 1)
                        + "，实际：" + _steps[i].Version);
                }
            }

            long current = await _versionStore.ReadCurrentAsync(ct);
            if (targetVersion < current)
            {
                return new MigrationOutcome.Rejected(
                    "目标版本 " + targetVersion + " 低于当前版本 " + current + "——不静默降级");
            }
            if (targetVersion == current)
            {
                return new MigrationOutcome.UpToDate(current);
            }
            if (targetVersion > _steps.Count)
            {
                return new MigrationOutcome.Rejected(
                    "目标版本 " + targetVersion + " 超出步骤表覆盖（共 " + _steps.Count + " 步）");
            }

            var applied = new List<long>();
            foreach (IMigrationStep step in _steps)
            {
                if (step.Version > targetVersion)
                {
                    break;
                }
                if (step.Version <= current)
                {
                    continue;
                }

                try
                {
                    await step.MigrateAsync(ct);
                    // 确认点：每步成功即写回——任意一步后进程终止，重启都从最后确认版本继续
                    await _versionStore.WriteAsync(step.Version, ct);
                }
                catch (Exception ex)
                {
                    return await RollbackAsync(step, applied, ex.Message, ct);
                }

                applied.Add(step.Version);
            }

            return new MigrationOutcome.Completed(applied);
        }

        /// <summary>
        /// 逆序回滚：先失败步（可能部分应用），再已确认步骤逐个回退；每个成功回退把版本存储
        /// 退回一格。任一回退失败即置 RollbackFailed 并继续尽力回退——最终以失败显式报告，
        /// 宿主据此拒绝启动，不得带半迁移状态运行。
        /// </summary>
        private async ValueTask<MigrationOutcome> RollbackAsync(
            IMigrationStep failedStep, List<long> applied, string reason, CancellationToken ct)
        {
            bool rollbackFailed = false;

            // 失败步从未被确认（版本存储未写回过它），回滚它不涉及版本存储
            try
            {
                await failedStep.RollbackAsync(ct);
            }
            catch
            {
                rollbackFailed = true;
            }

            for (int i = applied.Count - 1; i >= 0; i--)
            {
                IMigrationStep step = _steps[(int)(applied[i] - 1)];
                try
                {
                    await step.RollbackAsync(ct);
                    await _versionStore.WriteAsync(applied[i] - 1, ct);
                }
                catch
                {
                    rollbackFailed = true;
                }
            }

            long recovered;
            try
            {
                recovered = await _versionStore.ReadCurrentAsync(ct);
            }
            catch
            {
                // 版本存储不可达：RecoveredVersion 是"尽力报告值"——未知以 -1 表示，
                // 并保守标记回滚失败（状态不可信；宿主仍以失败拒绝启动）
                recovered = -1;
                rollbackFailed = true;
            }
            return new MigrationOutcome.Failed(failedStep.Version, reason, recovered, rollbackFailed);
        }
    }
}
