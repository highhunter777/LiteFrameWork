using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteFramework
{
    /// <summary>
    /// 恢复动作的实现端口（由装配点注入；执行器只调用、不实现）。
    /// </summary>
    public interface IRecoveryActions
    {
        /// <summary>导出诊断（错误原因 + 环境事实）；返回导出位置或 null（仅日志）。</summary>
        UniTask<string> ExportDiagnosticsAsync(string reason, CancellationToken ct);

        /// <summary>安全退出（编辑器内通常为 no-op/仅日志，由实现决定）。</summary>
        UniTask ExitAsync(CancellationToken ct);

        /// <summary>
        /// 清受控内容缓存（下载产物 + 激活记录回退 builtin；内置包根不动）。
        /// 返回 true = 已清理（重启后以内置/重新下载内容启动）；false = 未接实现或清理失败（已记日志）。
        /// </summary>
        UniTask<bool> ClearContentCacheAsync(CancellationToken ct);
    }
}
