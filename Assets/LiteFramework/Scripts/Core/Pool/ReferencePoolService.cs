using System;
using System.Collections.Generic;

namespace LiteFramework
{
    /// <summary>
    /// 引用池实例实现（B0）：把原静态桶搬进实例，按持有者/作用域管理生命周期。
    /// 契约与原 <see cref="ReferencePool"/> 一致：Release 时 Clear、池满按 per-type 淘汰、
    /// Debug 下检测重复归还；主线程 only。`Dispose` = 清空全部空闲件（在途件按归还契约回收）。
    /// </summary>
    public sealed class ReferencePoolService : IReferencePool, IModuleStats, IDisposable
    {
        /// <summary>每类型池容量上限默认值（与静态门面常量同源）。</summary>
        public const int DefaultMaxSize = 64;

        private readonly Dictionary<Type, Bucket> _buckets = new Dictionary<Type, Bucket>(32);
        private bool _disposed;

        private sealed class Bucket
        {
            public readonly Queue<IReference> Pool = new Queue<IReference>(4);
            public int MaxSize = DefaultMaxSize;
            public PoolEviction Eviction = PoolEviction.DropNewest;
            /// <summary>入池时刻（ms，Environment.TickCount——仅作收缩启发，不进任何判定逻辑）。
            /// 与 Pool 同进出序；TickCount 约 24.9 天回绕一次，回绕窗口内差值判读可能错一拍（无害启发）。</summary>
            public readonly Queue<long> AtMs = new Queue<long>(4);
            public long CreatedCount;
            public long AcquireCount;
            public long ReleaseCount;
            public int PeakUnused;
            public long DroppedCount;
            public long EvictedCount;
            public long PoolHits;
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
            public readonly HashSet<IReference> Using = new HashSet<IReference>(4);   // 重复归还检测——Debug-only
#endif
        }

        private Bucket GetOrAdd(Type type)
        {
            if (_buckets.TryGetValue(type, out Bucket bucket)) return bucket;
            return _buckets[type] = new Bucket();
        }

        public T Acquire<T>() where T : class, IReference, new()
        {
            Bucket bucket = GetOrAdd(typeof(T));
            T obj;
            if (bucket.Pool.Count > 0)
            {
                obj = (T)bucket.Pool.Dequeue();
                bucket.AtMs.Dequeue();
                bucket.PoolHits++;
            }
            else
            {
                obj = new T();
                bucket.CreatedCount++;
            }
            bucket.AcquireCount++;
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
            bucket.Using.Add(obj);
#endif
            return obj;
        }

        public void Release(IReference reference)
        {
            if (reference == null) throw new ArgumentNullException(nameof(reference));
            Bucket bucket = GetOrAdd(reference.GetType());
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
            if (!bucket.Using.Remove(reference))
                throw new InvalidOperationException($"ReferencePool: 重复归还或非 Acquire 所得:{reference.GetType().Name}");
#endif
            bucket.ReleaseCount++;
            try { reference.Clear(); }
            catch (Exception ex)
            {
                Log.Error(ex, $"ReferencePool.{reference.GetType().Name}.Clear");  // 作者代码失败：丢弃，不入池污染
                return;
            }

            if (bucket.Pool.Count >= bucket.MaxSize)
            {
                if (bucket.Eviction == PoolEviction.EvictOldest && bucket.Pool.Count > 0)
                {
                    bucket.EvictedCount++;
                    bucket.Pool.Dequeue();
                    bucket.AtMs.Dequeue();
                }
                else
                {
                    bucket.DroppedCount++;
                    return;
                }
            }
            bucket.Pool.Enqueue(reference);
            bucket.AtMs.Enqueue(Environment.TickCount);
            if (bucket.Pool.Count > bucket.PeakUnused) bucket.PeakUnused = bucket.Pool.Count;
        }

        public void Add<T>(int count) where T : class, IReference, new()
        {
            if (count <= 0) return;
            Bucket bucket = GetOrAdd(typeof(T));
            if (count > bucket.MaxSize) bucket.MaxSize = count;
            for (int i = 0; i < count; i++)
            {
                IReference obj = new T();
                bucket.CreatedCount++;
                try { obj.Clear(); } catch { /* 预热不因单个对象缺陷中断 */ }
                bucket.Pool.Enqueue(obj);
                bucket.AtMs.Enqueue(Environment.TickCount);
            }
            if (bucket.Pool.Count > bucket.PeakUnused) bucket.PeakUnused = bucket.Pool.Count;
        }

        public void Remove<T>(int count) where T : class, IReference, new()
        {
            if (count <= 0) return;
            Bucket bucket = GetOrAdd(typeof(T));
            while (count-- > 0 && bucket.Pool.Count > 0)
            {
                bucket.Pool.Dequeue();
                bucket.AtMs.Dequeue();
            }
        }

        public void Clear<T>() where T : class, IReference, new()
        {
            if (_buckets.TryGetValue(typeof(T), out Bucket bucket))
            {
                bucket.Pool.Clear();
                bucket.AtMs.Clear();
            }
        }

        public void ClearAll()
        {
            foreach (Bucket bucket in _buckets.Values)
            {
                bucket.Pool.Clear();
                bucket.AtMs.Clear();
            }
        }

        public int TrimExpired<T>(long maxIdleMs) where T : class, IReference, new()
        {
            if (!_buckets.TryGetValue(typeof(T), out Bucket bucket)) return 0;
            long now = Environment.TickCount;
            int trimmed = 0;
            while (bucket.Pool.Count > 0 && now - bucket.AtMs.Peek() > maxIdleMs)
            {
                bucket.Pool.Dequeue();
                bucket.AtMs.Dequeue();
                trimmed++;
            }
            return trimmed;
        }

        public int TrimExpired(long maxIdleMs)
        {
            long now = Environment.TickCount;
            int trimmed = 0;
            foreach (Bucket bucket in _buckets.Values)
            {
                while (bucket.Pool.Count > 0 && now - bucket.AtMs.Peek() > maxIdleMs)
                {
                    bucket.Pool.Dequeue();
                    bucket.AtMs.Dequeue();
                    trimmed++;
                }
            }
            return trimmed;
        }

        public void SetMaxSize<T>(int maxSize) where T : class, IReference, new()
        {
            if (maxSize < 0) throw new ArgumentOutOfRangeException(nameof(maxSize));
            GetOrAdd(typeof(T)).MaxSize = maxSize;
        }

        public void SetEviction<T>(PoolEviction eviction) where T : class, IReference, new()
        {
            GetOrAdd(typeof(T)).Eviction = eviction;
        }

        public string StatsName => "ReferencePool";

        public void Snapshot(Dictionary<string, string> into)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));
            into.Clear();
            int unused = 0;
            int usingCount = 0;
            long created = 0;
            long acquired = 0;
            long released = 0;
            long hits = 0;
            foreach (Bucket bucket in _buckets.Values)
            {
                unused += bucket.Pool.Count;
                created += bucket.CreatedCount;
                acquired += bucket.AcquireCount;
                released += bucket.ReleaseCount;
                hits += bucket.PoolHits;
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
                usingCount += bucket.Using.Count;
#endif
            }
            into["类型数"] = _buckets.Count.ToString();
            into["空闲"] = unused.ToString();
            into["在用"] = usingCount.ToString();
            into["已创建"] = created.ToString();
            into["获取"] = acquired.ToString();
            into["归还"] = released.ToString();
            into["池命中"] = hits.ToString();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ClearAll();
            _buckets.Clear();
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
        /// <summary>HUD 调用；每次分配一个列表（低频轮询可接受）。</summary>
        public IReadOnlyList<ReferencePoolInfo> GetAllInfos()
        {
            var list = new List<ReferencePoolInfo>(_buckets.Count);
            foreach (KeyValuePair<Type, Bucket> kv in _buckets)
            {
                Bucket bucket = kv.Value;
                list.Add(new ReferencePoolInfo(kv.Key, bucket.Pool.Count, bucket.Using.Count,
                    bucket.CreatedCount, bucket.AcquireCount, bucket.ReleaseCount,
                    bucket.MaxSize, bucket.PeakUnused, bucket.DroppedCount,
                    bucket.EvictedCount, bucket.PoolHits));
            }
            return list;
        }
#endif
    }
}
