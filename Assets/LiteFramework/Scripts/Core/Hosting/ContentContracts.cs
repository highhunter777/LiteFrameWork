using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteFramework
{
    /// <summary>
    /// 内容代次（《商业级通用客户端框架总设计》§8.2"Scope 固定 ContentGeneration，加载与缓存键包含代次"；
    /// 《热更与内容发布专项设计》§9"ContentGeneration 持有固定资源目录、脚本集合、配置快照及依赖 ID"）：
    /// 同一 location 在不同代次下是**不同资源**——候选与当前隔离、旧使用者不从全局 Current 混取新内容的键维度。
    /// 本类型是代次的**键身份**（ReleaseId + 单调 Value）；完整代次对象（目录/脚本集合/快照/依赖 ID）随热更批在激活事务上构建。
    /// </summary>
    public readonly struct ContentGeneration : IEquatable<ContentGeneration>
    {
        /// <summary>发布身份（热更 §5 ReleaseId：一次不可变内容发布；内置内容为 "builtin"）。</summary>
        public string ReleaseId { get; }

        /// <summary>同 Release 内单调代次（内置/初始 = 0；候选激活递增）。</summary>
        public ulong Value { get; }

        /// <summary>内置代次（无热更、始终可用——启动与恢复的兜底身份）。</summary>
        public static ContentGeneration Default { get; } = new ContentGeneration("builtin", 0);

        public ContentGeneration(string releaseId, ulong value)
        {
            ReleaseId = releaseId ?? throw new ArgumentNullException(nameof(releaseId));
            Value = value;
        }

        public bool Equals(ContentGeneration other)
            => Value == other.Value && string.Equals(ReleaseId, other.ReleaseId, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is ContentGeneration other && Equals(other);

        public override int GetHashCode()
            => (ReleaseId == null ? 0 : StringComparer.Ordinal.GetHashCode(ReleaseId)) ^ Value.GetHashCode();

        public override string ToString() => $"{ReleaseId}#{Value}";
    }

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

        /// <summary>获取资源租约（引用计数 +1；失败抛含 location 的异常；ct 取消贯穿加载）。
        /// <paramref name="generation"/> 固定本次获取的**内容代次**（§8.2：加载/缓存键包含代次——
        /// 默认 = 当前代；旧 Scope 应显式携带自己的代次，不从全局 Current 混取新内容）。</summary>
        UniTask<AssetLease<T>> AcquireAsync<T>(string location, ContentGeneration generation = default, CancellationToken ct = default) where T : class;

        /// <summary>优雅关闭：释放全部剩余租约与缓存（幂等）。</summary>
        UniTask ShutdownAsync(CancellationToken ct = default);
    }
}
