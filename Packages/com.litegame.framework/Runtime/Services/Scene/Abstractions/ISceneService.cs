using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteFramework
{
    /// <summary>
    /// 场景服务契约（**场景能力归 Client.Runtime， YooAsset 实现归 Adapter**；先形成逻辑边界和依赖测试）。
    /// </summary>
    public interface ISceneService
    {
        /// <summary>该 location 是否已加载（单场景或叠加任一）。</summary>
        bool IsLoaded(string location);

        /// <summary>当前单场景名（无则 null）。</summary>
        string SingleSceneName { get; }

        /// <summary>当前叠加场景数。</summary>
        int AdditiveCount { get; }

        /// <summary>已加载叠加场景的 location 只读视图（诊断/面板用）。</summary>
        IReadOnlyCollection<string> AdditiveLocations { get; }

        /// <summary>
        /// 最近一次加载操作的进度（0~1；无加载为 0）——通用加载页窗口④的场景进度源
        /// （《通用加载页设计》§1；消费方在 OnUpdate 轮询后 Report，不建每帧泵）。
        /// </summary>
        float Progress { get; }

        /// <summary>加载单场景并激活（先卸旧单场景；叠加登记随之清理）。</summary>
        UniTask LoadSingleAsync(string location, CancellationToken ct = default);

        /// <summary>卸载当前单场景；无场景 = no-op（幂等宽容）。</summary>
        UniTask UnloadSingleAsync(CancellationToken ct = default);

        /// <summary>叠加加载场景并激活；同 location 已加载 = 警告 + no-op。</summary>
        UniTask LoadAdditiveAsync(string location, CancellationToken ct = default);

        /// <summary>卸载指定叠加场景；未登记 = no-op（幂等宽容）。</summary>
        UniTask UnloadAdditiveAsync(string location, CancellationToken ct = default);

        /// <summary>释放全部在持场景句柄（宿主关闭面；幂等）。之后拒绝新加载。</summary>
        void ReleaseAll();
    }
}
