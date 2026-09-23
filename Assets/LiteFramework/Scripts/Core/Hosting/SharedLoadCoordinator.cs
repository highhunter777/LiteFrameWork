using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteFramework
{
    /// <summary>
    /// 共享加载协调器（《商业级通用客户端框架总设计》§8.2 SharedLoad——同 generation/location/type 的并发请求合并；
    /// 《热更与内容发布专项设计》§7 取消语义逐条落点）：
    ///
    /// - **合并**：同 key 的多个获取者共享**一次**底层加载（loader 只被调用一次）；加载完成后，新获取者走
    ///   已完成路径（同步拿到租约，不重复加载）。
    /// - **等待者取消 ≠ 底层取消**：取消单个等待者只退出**本人**等待（其余等待者不受影响）；
    ///   全部等待者退出后该次加载被标记**弃置**（键移除、可重试）并尽力取消底层（loader 收到的取消源——
    ///   YooAsset 操作不可中途取消，故"迟到结果不复活"由完成续延兜底：迟到的成功结果就地卸载，不发出租约）。
    /// - **失败**：全部等待者收到同一异常；键移除（重试 = 新的一次加载）。
    /// - **引用计数**：Acquire 返回的租约即一份持有；全部持有者释放后 unloader 恰好执行一次
    ///   （引用归零即移除——**无保留缓存**；缓存预算/保留策略归 U1/热更批，本类不假装有）。
    /// - **释放面**：<see cref="Dispose"/> = 关闭路径：已加载条目就地卸载、在途加载尽力取消、迟到结果丢弃。
    ///
    /// 线程模型：全部状态变更在锁内；loader/unloader 一律在锁外调用（实现方可回调本类而无死锁）。
    /// </summary>
    public sealed class SharedLoadCoordinator<TKey, TAsset> : IDisposable
        where TKey : IEquatable<TKey>
        where TAsset : class
    {
        private sealed class Entry
        {
            public TKey Key;
            public UniTaskCompletionSource<TAsset> LoadSource;   // 底层加载承诺（创建时即挂上——晚到的等待者总能 join）
            public CancellationTokenSource LoadCts;              // 底层取消源（尽力——不可取消的实现方无害）
            public TAsset Asset;                                  // 成功后非空（首个恢复的等待者填）
            public bool Loaded;
            public bool Orphaned;                                 // 全员退出/已释放——迟到结果不复活
            public bool Unloaded;                                 // 卸载恰好一次的守卫
            public int Waiters;                                   // 仍在等待的获取者
            public int Holders;                                   // 已发出租约的持有者
            public int Total => Waiters + Holders;
        }

        private readonly object _gate = new object();
        private readonly Dictionary<TKey, Entry> _entries = new Dictionary<TKey, Entry>();
        private readonly Func<TKey, CancellationToken, UniTask<TAsset>> _loader;
        private readonly Action<TKey, TAsset> _unloader;
        private bool _disposed;

        /// <summary>当前存活条目数（在途等待 + 有效持有 > 0 的键——诊断/泄漏断言用）。</summary>
        public int LiveEntryCount { get { lock (_gate) return _entries.Count; } }

        /// <param name="loader">底层加载（同 key 只调一次；收到取消源应尽力可取消——不可取消则允许迟到完成）。</param>
        /// <param name="unloader">底层卸载（引用归零/弃置迟到结果/Dispose 时恰好执行一次）。</param>
        public SharedLoadCoordinator(Func<TKey, CancellationToken, UniTask<TAsset>> loader, Action<TKey, TAsset> unloader)
        {
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
            _unloader = unloader ?? throw new ArgumentNullException(nameof(unloader));
        }

        /// <summary>获取一份共享加载的租约：在途合并、完成复用、失败穿透、等待者取消只退本人。</summary>
        public UniTask<AssetLease<TAsset>> AcquireAsync(TKey key, CancellationToken ct = default)
        {
            Entry entry;
            bool startLoad = false;
            lock (_gate)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(SharedLoadCoordinator<TKey, TAsset>));
                if (!_entries.TryGetValue(key, out entry))
                {
                    entry = new Entry
                    {
                        Key = key,
                        LoadSource = new UniTaskCompletionSource<TAsset>(),
                        LoadCts = new CancellationTokenSource(),
                    };
                    entry.LoadSource.Task.ContinueWith(asset => OnLateCompleted(entry, asset)).Forget();   // 迟到结果兜底（成功路径才会触发）
                    _entries[key] = entry;
                    startLoad = true;
                }
                entry.Waiters++;
            }

            if (startLoad) RunLoaderAsync(entry).Forget();       // 锁外启动：loader 可回调本类

            return WaitAsync(entry, ct);
        }

        /// <summary>关闭释放面：已加载条目就地卸载、在途加载尽力取消、迟到结果丢弃（幂等）。</summary>
        public void Dispose()
        {
            List<Entry> toUnload = null;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                foreach (var kv in _entries)
                {
                    Entry e = kv.Value;
                    e.Orphaned = true;
                    try { e.LoadCts.Cancel(); } catch (ObjectDisposedException) { }
                    if (e.Loaded && e.Asset != null && !e.Unloaded)
                    {
                        e.Unloaded = true;
                        (toUnload ??= new List<Entry>()).Add(e);
                    }
                }
                _entries.Clear();
            }

            if (toUnload != null)
                for (int i = toUnload.Count - 1; i >= 0; i--)      // 逆序（后加载先卸）
                    _unloader(toUnload[i].Key, toUnload[i].Asset);
        }

        // ---- 内部 ----

        private async UniTaskVoid RunLoaderAsync(Entry entry)
        {
            try
            {
                TAsset asset = await _loader(entry.Key, entry.LoadCts.Token);
                entry.LoadSource.TrySetResult(asset);
            }
            catch (Exception ex)
            {
                entry.LoadSource.TrySetException(ex);
            }
        }

        private async UniTask<AssetLease<TAsset>> WaitAsync(Entry entry, CancellationToken ct)
        {
            TAsset asset;
            try
            {
                // 外部取消只约束本次等待（AttachExternalCancellation 不触碰底层任务——底层由全员退出/Dispose 决定）
                asset = await entry.LoadSource.Task.AttachExternalCancellation(ct);
            }
            catch
            {
                OnWaiterGone(entry);                             // 取消/失败：退出本人等待份额
                throw;                                           // 原样穿透（OCE 与加载失败语义不同，调用方区分）
            }

            lock (_gate)
            {
                entry.Waiters--;
                if (entry.Orphaned)
                {
                    // 等待恢复时键已被弃置（其余等待者全员退出后本次加载被标记）——迟到结果不复活，不发出租约。
                    // 资产由完成续延（OnLateCompleted）卸载；本等待份额已在弃置时清账，此处只拒绝交付。
                    throw new ObjectDisposedException(
                        $"共享加载键 {entry.Key} 已被弃置——迟到结果不发出租约（热更专项 §7）");
                }
                entry.Asset ??= asset;                           // 首个恢复的等待者填充（后续同值幂等）
                entry.Loaded = true;
                entry.Holders++;                                 // 等待份额转为持有份额（租约即持有）
            }

            return new AssetLease<TAsset>(entry.Key.ToString(), asset, lease => Release(entry, lease));
        }

        /// <summary>等待者退出（取消/失败）：全员清零则弃置该次加载（移除 + 尽力取消 + 迟到结果由续延卸载）。</summary>
        private void OnWaiterGone(Entry entry)
        {
            lock (_gate)
            {
                entry.Waiters--;
                if (entry.Total == 0 && _entries.TryGetValue(entry.Key, out Entry current) && ReferenceEquals(current, entry))
                {
                    _entries.Remove(entry.Key);
                    entry.Orphaned = true;
                    try { entry.LoadCts.Cancel(); } catch (ObjectDisposedException) { }
                }
            }
        }

        /// <summary>租约释放（AssetLease.Dispose 回调）：末位持有者触发键移除 + 卸载恰好一次。</summary>
        private void Release(Entry entry, AssetLease<TAsset> lease)
        {
            TAsset assetToUnload = null;
            lock (_gate)
            {
                entry.Holders--;
                if (entry.Total == 0 && _entries.TryGetValue(entry.Key, out Entry current) && ReferenceEquals(current, entry))
                {
                    _entries.Remove(entry.Key);
                    if (entry.Loaded && entry.Asset != null && !entry.Unloaded)
                    {
                        entry.Unloaded = true;
                        assetToUnload = entry.Asset;
                    }
                }
            }

            if (assetToUnload != null)
                _unloader(entry.Key, assetToUnload);             // 锁外：unloader 可回调本类（新键新生命周期）
        }

        /// <summary>加载完成续延：全员弃置后的迟到成功结果就地卸载（不复活、不发出租约）。</summary>
        private void OnLateCompleted(Entry entry, TAsset asset)
        {
            TAsset assetToUnload = null;
            lock (_gate)
            {
                if (entry.Orphaned && !entry.Unloaded && entry.Asset == null)
                {
                    entry.Unloaded = true;
                    assetToUnload = asset;
                }
            }

            if (assetToUnload != null)
                _unloader(entry.Key, assetToUnload);
        }
    }
}
