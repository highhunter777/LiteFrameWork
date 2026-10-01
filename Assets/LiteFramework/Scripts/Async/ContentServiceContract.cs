using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteFramework
{
    /// <summary>
    /// 内容服务端口（《商业级通用客户端框架总设计》§8.1：用可注入 IContentService 取代静态所有权；
    /// §5.1 LiteClient.Abstractions 生命周期/内容接口——本批先在 LiteFramework.Core 形成逻辑边界）。
    ///
    /// 实现方职责（§8.2 落点）：
    /// - InitializeAsync 幂等（重复调用直接返回）。
    /// - AcquireAsync 成功 → 返回带引用计数的租约；失败 → 抛（含 location 诊断）。
    /// - ct 取消贯穿加载全程；取消后不得残留部分加载状态。
    /// - ShutdownAsync 释放全部剩余租约与缓存（§4.4 Shutdown 语义）。
    /// YooAsset 适配与 Offline/Host 模式归适配层（§5.1 Content.YooAsset）；本接口不感知后端。
    ///
    /// **归属（《客户端与服务端共享代码范围专项设计》§7-4 批④）**：本端口用 <c>UniTask</c> 表达
    /// 异步契约，故随 `LiteFramework.Async` 程序集落位，**不在** `LiteFramework.Core`（S2 契约层，
    /// 义务是零第三方实现依赖、可被服务端引用）。同文件的 <see cref="ContentGeneration"/> 与
    /// <see cref="AssetLease{T}"/> **零异步依赖**，留在 Core——`ActivationTransactionStore` 消费前者，
    /// 不必反向依赖 Async。
    /// </summary>
    public interface IContentService
    {
        /// <summary>初始化（幂等）。</summary>
        UniTask InitializeAsync(CancellationToken ct = default);

        /// <summary>获取资源租约（引用计数 +1；失败抛含 location 的异常；ct 取消贯穿加载）。
        /// <paramref name="generation"/> 固定本次获取的**内容代次**（§8.2：加载/缓存键包含代次——
        /// 默认 = 当前代；旧 Scope 应显式携带自己的代次，不从全局 Current 混取新内容）。</summary>
        UniTask<AssetLease<T>> AcquireAsync<T>(string location, ContentGeneration generation = default, CancellationToken ct = default) where T : class;

        /// <summary>
        /// 按 tag 列出内容路径（如 "lua" → 全部 .lua 资产路径）。
        ///
        /// **为什么在契约里**：TAG 是**发布约定**（收集组打标），实现方各不相同
        /// （YooAsset 走 <c>GetAssetInfos</c>；热更批走发布清单）。放在内容服务契约里，
        /// 调用方（装配点）不必认识任何具体后端的包类型——原先装配点直调
        /// `AssetService.Package`（YooAsset 类型），拆 asmdef 时就是 Runtime → Adapter 的硬依赖。
        /// </summary>
        IReadOnlyList<string> ListAssetPathsByTag(string tag);

        /// <summary>优雅关闭：释放全部剩余租约与缓存（幂等）。</summary>
        UniTask ShutdownAsync(CancellationToken ct = default);
    }
}
