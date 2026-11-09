using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteFramework
{
    /// <summary>
    /// 内容激活端口：安全窗口内"停止接受受影响的新操作，退出相关 Match/Scene/UI Scope， 取消在途请求，解除旧逻辑回调与资源引用"。
    /// </summary>
    public interface IContentActivator
    {
        /// <summary>在安全窗口内切换到候选内容。</summary>
        UniTask ActivateAsync(ReleaseManifest candidate, CancellationToken ct = default);

        /// <summary>健康检查失败时重建到已确认版本（从允许的 Confirmed 版本重建，不使用半成品）。</summary>
        UniTask RebuildConfirmedAsync(ContentGeneration confirmed, CancellationToken ct = default);
    }
}
