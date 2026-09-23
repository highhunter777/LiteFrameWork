using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteFramework
{
    /// <summary>
    /// 资源租约（《商业级通用客户端框架总设计》§8.2 资源租约）：对已加载资产的一份**可释放持有权**。
    ///
    /// 契约（§8.2 逐条落点）：
    /// - **对称释放**：Acquire 成功 → Release 恰好一次；重复 Release 幂等（不抛）。
    /// - **引用计数由 Host（内容服务实现方）维护**：租约只是持有权的令牌——Release 通知服务方递减；
    ///   最后一个使用者 Release 后，服务方才可卸载底层资产（Prefab 租约从加载完成持有至最后一个依赖实例销毁）。
    /// - **资产对象在 Release 后不可再使用**（<see cref="Asset"/> 读数由实现方决定置 null 或保留——
    ///   消费方必须在 Release 前取好引用；异步回调写入前核对代次/有效性）。
    /// - 泛型参数 **不约束 UnityEngine.Object**（Core 零引擎依赖）；Unity 侧以 <c>AssetLease&lt;GameObject&gt;</c> 使用。
    /// </summary>
    public sealed class AssetLease<T> : IDisposable where T : class
    {
        private readonly Action<AssetLease<T>> _releaseCallback;
        private bool _released;

        /// <summary>租约标识（location 或缓存键；诊断/断言用）。</summary>
        public string Key { get; }

        /// <summary>持有的资产对象（实现方保证 Release 后不可再依赖；是否置 null 由实现方决定）。</summary>
        public T Asset { get; }

        /// <summary>是否已释放。</summary>
        public bool IsReleased { get { lock (this) return _released; } }

        /// <summary>实现方工厂缝（IContentService 实现方创建租约；消费方只 Dispose 不构造）。</summary>
        public AssetLease(string key, T asset, Action<AssetLease<T>> releaseCallback)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Asset = asset;
            _releaseCallback = releaseCallback ?? throw new ArgumentNullException(nameof(releaseCallback));
        }

        /// <summary>释放租约（幂等；仅首次触发回调通知服务方递减引用）。</summary>
        public void Dispose()
        {
            lock (this)
            {
                if (_released) return;
                _released = true;
            }
            _releaseCallback(this);
        }
    }

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
    /// </summary>
    public interface IContentService
    {
        /// <summary>初始化（幂等）。</summary>
        UniTask InitializeAsync(CancellationToken ct = default);

        /// <summary>获取资源租约（引用计数 +1；失败抛含 location 的异常；ct 取消贯穿加载）。</summary>
        UniTask<AssetLease<T>> AcquireAsync<T>(string location, CancellationToken ct = default) where T : class;

        /// <summary>优雅关闭：释放全部剩余租约与缓存（幂等）。</summary>
        UniTask ShutdownAsync(CancellationToken ct = default);
    }
}
