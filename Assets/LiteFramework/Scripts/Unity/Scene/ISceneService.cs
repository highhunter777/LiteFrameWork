using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteFramework
{
    /// <summary>
    /// 场景服务契约（**场景能力归 Client.Runtime，
    /// YooAsset 实现归 Adapter**；先形成逻辑边界和依赖测试）。
    ///
    /// **住 LiteFramework.Unity 而非 LiteGame**：与 `INetworkService` 同类——
    /// 框架侧的**服务端口**。放在游戏侧会让"适配器程序集实现它"必须反向引用游戏程序集；
    /// 框架侧则天然被两边引用（`LiteFramework.Core` 被适配器引、`LiteFramework.Unity` 被 Runtime 引）。
    ///
    /// **为什么需要这层抽象**：`SceneService` 是 YooAsset 适配器（持 `SceneHandle`），
    /// 而 `ProcedureLaunch` 是 Client.Runtime 的流程——若直接依赖 `SceneService` 具体类
    /// （即使只用它做容器注册），拆 asmdef 时那条边就是 `Runtime → Adapter` 的硬依赖。
    /// 补本接口后：流程认识契约，装配根注入实现。
    ///
    /// **与 `ConfigService`/`IConfigService` 的关系（一处刻意的偏离）**：那一对把接口与实现
    /// 放在同一文件。本接口**不跟随**——若也住在适配器目录，
    /// 拆 asmdef 后接口会被困在适配器程序集里，`Runtime` 仍然够不到它。
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
