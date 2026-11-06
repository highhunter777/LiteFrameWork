using System;

namespace LiteFramework
{
    /// <summary>
    /// 内容代次：同一 location 在不同代次下是**不同资源**——候选与当前隔离、旧使用者不从全局 Current 混取新内容的键维度。
    /// 本类型是代次的**键身份**（ReleaseId + 单调 Value）；完整代次对象（目录/脚本集合/快照/依赖 ID）在激活事务上构建。
    /// </summary>
    public readonly struct ContentGeneration : IEquatable<ContentGeneration>
    {
        /// <summary>内置内容的发布身份（无热更兜底；激活记录初值同源）。</summary>
        public const string BuiltinReleaseId = "builtin";

        /// <summary>发布身份（ReleaseId：一次不可变内容发布；内置内容为 <see cref="BuiltinReleaseId"/>）。</summary>
        public string ReleaseId { get; }

        /// <summary>同 Release 内单调代次（内置/初始 = 0；候选激活递增）。</summary>
        public ulong Value { get; }

        /// <summary>内置代次（无热更、始终可用——启动与恢复的兜底身份）。</summary>
        public static ContentGeneration Default { get; } = new ContentGeneration(BuiltinReleaseId, 0);

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
    /// 资源租约：对已加载资产的一份**可释放持有权**。
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
}
