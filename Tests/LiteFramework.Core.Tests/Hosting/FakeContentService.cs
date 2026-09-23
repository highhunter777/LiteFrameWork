using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteFramework
{
    /// <summary>
    /// IContentService 的 fake 实现（C1-⑤ 接缝批：契约冻结 + 失败注入夹具）。
    /// 真实实现归 YooAsset 适配层（C1-⑥/后续）；本类服务三件事：
    /// ① L1 契约测试（租约对称/引用计数/失败语义）②消费方（UIService/VFX 等）接缝验证 ③失败注入演示。
    ///
    /// 语义：
    /// - location → 资产的映射由 <see cref="Register{T}"/> 预登记（string key → object）；
    ///   未登记的 location 抛 KeyNotFoundException（fail-fast）。
    /// - 失败注入：<see cref="InjectFailure"/> 命中的 location，AcquireAsync 抛注入的异常（原样上抛）。
    /// - ShutdownAsync：释放全部未释放租约（逆序）+ 幂等。
    /// </summary>
    public sealed class FakeContentService : IContentService
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, object> _assets = new Dictionary<string, object>();
        private readonly Dictionary<string, Exception> _injected = new Dictionary<string, Exception>(StringComparer.Ordinal);
        private readonly List<IDisposable> _liveLeases = new List<IDisposable>();   // AssetLease<T> 实现 IDisposable
        private int _initialized;                                            // 0=未 1=已
        private bool _shutdown;

        /// <summary>预登记 location → 资产（测试播种口）。</summary>
        public void Register<T>(string location, T asset) where T : class
        {
            lock (_gate) _assets[location] = asset;
        }

        /// <summary>失败注入：命中的 location 在 AcquireAsync 时抛注入的异常（原样上抛）。</summary>
        public void InjectFailure(string location, Exception failure)
        {
            if (failure == null) throw new ArgumentNullException(nameof(failure));
            lock (_gate) _injected[location] = failure;
        }

        /// <summary>当前未释放租约数（泄漏断言用）。</summary>
        public int LiveLeaseCount { get { lock (_gate) return _liveLeases.Count; } }

        /// <summary>Shutdown 已执行标记（幂等断言用）。</summary>
        public bool ShutdownCompleted { get; private set; }

        public UniTask InitializeAsync(CancellationToken ct = default)
        {
            Interlocked.Exchange(ref _initialized, 1);
            return UniTask.CompletedTask;
        }

        public UniTask<AssetLease<T>> AcquireAsync<T>(string location, CancellationToken ct = default) where T : class
        {
            if (Volatile.Read(ref _initialized) == 0)
                throw new InvalidOperationException($"FakeContentService 未初始化——先 InitializeAsync（{location}）");
            if (_shutdown) throw new ObjectDisposedException(nameof(FakeContentService));

            Exception injected;
            lock (_gate)
            {
                if (_injected.TryGetValue(location, out injected))
                    throw injected;                            // 失败注入：原样上抛（OCE/任意异常透传）
            }

            if (!_assets.TryGetValue(location, out object asset))
                throw new KeyNotFoundException($"FakeContentService:未登记的 location '{location}'");

            var lease = new AssetLease<T>(location, (T)asset, lease =>
            {
                lock (_gate) _liveLeases.Remove(lease);
            });
            lock (_gate) _liveLeases.Add(lease);
            return UniTask.FromResult(lease);
        }

        public UniTask ShutdownAsync(CancellationToken ct = default)
        {
            ShutdownCompleted = true;
            lock (_gate)
            {
                for (int i = _liveLeases.Count - 1; i >= 0; i--) _liveLeases[i].Dispose();   // 逆序释放
                _liveLeases.Clear();
                _shutdown = true;
            }
            return UniTask.CompletedTask;
        }
    }
}
