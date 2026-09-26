using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteGame
{
    /// <summary>
    /// 场景服务契约（《商业级通用客户端框架总设计》§5 目标架构：**场景能力归 Client.Runtime，
    /// YooAsset 实现归 Adapter**；§5.1"先形成逻辑边界和依赖测试"）。
    ///
    /// **为什么需要这层抽象**：`SceneService` 是 YooAsset 适配器（持 `SceneHandle`），
    /// 而 `ProcedureLaunch` 是 Client.Runtime 的流程——原先它直接依赖 `SceneService` 具体类
    /// （虽然只用它做容器注册）。拆 asmdef 时那条边就是 `Runtime → Adapter` 的硬依赖。
    /// 补本接口后：流程认识契约，装配根注入实现。
    ///
    /// **与 `ConfigService`/`IConfigService` 的关系（一处刻意的偏离）**：那一对把接口与实现
    /// 放在同一文件（`Shell/Resource/ConfigService.cs`）。本接口**不跟随**——若也住在适配器目录，
    /// 拆 asmdef 后接口会被困在适配器程序集里，`Runtime` 仍然够不到它。故本文件住 `Shell/` 根。
    /// （`IConfigService` 将来会撞上同一个问题，届时按同样办法搬。）
    ///
    /// **不含** `LifetimeToken`：那是实现内部的取消源，不是消费方契约
    /// （消费方要取消应传自己的 <c>CancellationToken</c>）。
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
