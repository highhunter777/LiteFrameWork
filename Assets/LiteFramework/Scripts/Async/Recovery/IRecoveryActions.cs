using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteFramework
{
    /// <summary>
    /// 恢复动作的实现端口（由装配点注入；执行器只调用、不实现）。
    /// 当前只收“导出诊断/退出”两类真实动作：清缓存/回登录依赖内容与账号的单点入口，随内容/会话接入补齐——
    /// 不在此造空实现（“能编译的空动作”会掩盖恢复链断裂）。
    /// </summary>
    public interface IRecoveryActions
    {
        /// <summary>导出诊断（错误原因 + 环境事实）；返回导出位置或 null（仅日志）。</summary>
        UniTask<string> ExportDiagnosticsAsync(string reason, CancellationToken ct);

        /// <summary>安全退出（编辑器内通常为 no-op/仅日志，由实现决定）。</summary>
        UniTask ExitAsync(CancellationToken ct);
    }
}
