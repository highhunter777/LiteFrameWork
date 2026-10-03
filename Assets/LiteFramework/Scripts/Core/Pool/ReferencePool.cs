using System;
using System.Collections;
using System.Collections.Generic;


namespace LiteFramework
{
    /// <summary>开发 HUD 数据源。整体随条件编译剥离——release 无此类型,HUD asmdef 同为 Development-only。</summary>
    public readonly struct ReferencePoolInfo
    {
        public readonly Type Type;
        public readonly int Unused;
        public readonly int Using;
        public readonly long CreatedCount;    // Acquire 未命中转 new 的次数（Misses = Acquired − PoolHits）
        public readonly long AcquireCount;
        public readonly long ReleaseCount;
        public readonly int MaxSize;        // 当前容量上限
        public readonly int PeakUnused;     // 池内数量的历史峰值
        public readonly long DroppedCount;  // DropNewest 路径：池满丢弃新归还件的次数
        public readonly long EvictedCount;  // EvictOldest 路径：池满淘汰队首最旧件的次数
        public readonly long PoolHits;      // 池中直取数；复用率 = PoolHits / AcquireCount

        public ReferencePoolInfo(Type type, int unused, int using_, long created, long acquire, long release,
                                 int maxSize = ReferencePool.DefaultMaxSize,
                                 int peakUnused = 0, long droppedCount = 0,
                                 long evictedCount = 0, long poolHits = 0)
        {
            Type = type; Unused = unused; Using = using_;
            CreatedCount = created; AcquireCount = acquire; ReleaseCount = release;
            MaxSize = maxSize; PeakUnused = peakUnused; DroppedCount = droppedCount;
            EvictedCount = evictedCount; PoolHits = poolHits;
        }
    }

    /// <summary>
    /// 引用池(静态门面豁免 #2,与 Log 同列;名单封闭)。
    /// 契约:入池对象刚被 Clear;Acquire 方不得假设任何字段为默认值——
    /// Release 时的 Clear 是防泄漏防御(解除外部引用),不是初始化服务,初始化责任始终在调用方。
    ///
    /// 高级化(《对象池专项设计》§7):计数无条件化(对齐内核"计数器不做条件编译"裁决——release 口径
    /// 与 Debug 一致);**保持 Debug-only 的两处**——Using 哈希集(重复归还检测,"release 信任 Debug 全绿"
    /// 契约不变)与 GetAllInfos/ReferencePoolInfo(HUD 面);per-type 淘汰策略 SetEviction(默认 DropNewest
    /// =现状;EvictOldest 淘汰队首最旧、新归还件入池——两计数不混用同内核 §3.1 口径);Clear/ClearAll
    /// 整体清空(被清件只丢弃不走第二次 Clear,与 Trim 同口径);TrimExpired 按龄收缩(§7.4——入池时刻为
    /// 系统钟仅作收缩启发、不进任何判定逻辑;调用方驱动,指定类型或跨全部桶)。
    /// 非目标(§7.5 登记):validateOnAcquire(纯 C# 数据无 fake-null 场景)。
    /// </summary>
    public static class ReferencePool
    {
        /// <summary>
        /// 每类型池容量上限默认值。**池的内存占用应与"活跃对象数"挂钩,而不是历史峰值**——
        /// 不设上限时,一次业务高峰(如进战斗)取用的对象会全部永久驻留。
        /// </summary>
        public const int DefaultMaxSize = 64;

        private static readonly Dictionary<Type, Bucket> s_buckets = new Dictionary<Type, Bucket>(32);

        private sealed class Bucket
        {
            public readonly Queue<IReference> Pool = new Queue<IReference>(4);
            // 容量控制与淘汰策略是运行期的内存保护,release 同样要生效,故不进条件编译
            public int MaxSize = DefaultMaxSize;
            public PoolEviction Eviction = PoolEviction.DropNewest;
            /// <summary>入池时刻（ms，Environment.TickCount——§7.4：仅作收缩启发，不进任何判定逻辑。
            /// 与 Pool 同进出序；TickCount 为 int 毫秒钟约 24.9 天回绕一次，回绕窗口内差值判读可能错一拍（无害启发）。</summary>
            public readonly Queue<long> AtMs = new Queue<long>(4);
            // 统计无条件化(§7.1):自增开销可忽略,release 口径与 Debug 一致
            public long CreatedCount;
            public long AcquireCount;
            public long ReleaseCount;
            public int PeakUnused;
            public long DroppedCount;
            public long EvictedCount;
            public long PoolHits;
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
            public readonly HashSet<IReference> Using = new HashSet<IReference>(4);   // 重复归还检测——Debug-only 诊断面
#endif
        }

        private static Bucket GetOrAdd(Type t)
        {
            if (s_buckets.TryGetValue(t, out var b)) return b;
            return s_buckets[t] = new Bucket();
        }

        /// <summary>取对象;空池则 new。返回状态未定义,调用方必须赋值全部业务字段后使用。</summary>
        public static T Acquire<T>() where T : class, IReference, new()
        {
            var bucket = GetOrAdd(typeof(T));
            T obj;
            if (bucket.Pool.Count > 0)
            {
                obj = (T)bucket.Pool.Dequeue();
                bucket.AtMs.Dequeue();                         // 时刻队列与 Pool 同进出序
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

        /// <summary>
        /// 归还;池负责 Clear。Debug 校验重复归还与未知类型;release 信任 Debug 全绿(重复归还为 UB)。
        /// 池满按 per-type 淘汰策略:DropNewest(默认)= 丢弃新归还件(DroppedCount);
        /// EvictOldest = 淘汰队首最旧、新归还件入池(EvictedCount)——对象已 Clear,交给 GC 即可,不无限驻留。
        /// </summary>
        public static void Release(IReference reference)
        {
            if (reference == null) throw new ArgumentNullException(nameof(reference));
            var bucket = GetOrAdd(reference.GetType());
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
            if (!bucket.Using.Remove(reference))
                throw new InvalidOperationException($"ReferencePool: 重复归还或非 Acquire 所得:{reference.GetType().Name}");
#endif
            bucket.ReleaseCount++;
            try { reference.Clear(); }
            catch (Exception ex)
            {
                Log.Error(ex, $"ReferencePool.{reference.GetType().Name}.Clear");  // Clear 是对象作者代码;炸了丢弃,不入池污染
                return;
            }

            if (bucket.Pool.Count >= bucket.MaxSize)
            {
                if (bucket.Eviction == PoolEviction.EvictOldest && bucket.Pool.Count > 0)
                {
                    bucket.EvictedCount++;
                    bucket.Pool.Dequeue();                      // 淘汰队首最旧(已 Clear),新归还件入池
                    bucket.AtMs.Dequeue();                       // 双队同步
                }
                else
                {
                    bucket.DroppedCount++;                     // 超限:丢弃新归还件,而非让池随峰值膨胀
                    return;
                }
            }
            bucket.Pool.Enqueue(reference);
            bucket.AtMs.Enqueue(Environment.TickCount);         // 入池时刻（§7.4 收缩启发）
            if (bucket.Pool.Count > bucket.PeakUnused) bucket.PeakUnused = bucket.Pool.Count;
        }

        /// <summary>预热:消除业务高峰(如进战斗)首帧的 new 尖峰。保留于 release——纯循环,无统计开销。
        /// **count 超过当前上限时自动上调上限**——预热即显式声明"我需要这么多容量"。</summary>
        public static void Add<T>(int count) where T : class, IReference, new()
        {
            if (count <= 0) return;
            var bucket = GetOrAdd(typeof(T));
            if (count > bucket.MaxSize) bucket.MaxSize = count;
            for (int i = 0; i < count; i++)
            {
                IReference obj = new T();
                bucket.CreatedCount++;                          // 预热也是"创建"(未 Acquire 不计命中/在用)
                try { obj.Clear(); } catch { /* 预热不因单个对象的 Clear 缺陷中断 */ }
                bucket.Pool.Enqueue(obj);
                bucket.AtMs.Enqueue(Environment.TickCount);
            }
            if (bucket.Pool.Count > bucket.PeakUnused) bucket.PeakUnused = bucket.Pool.Count;
        }

        /// <summary>裁剪池内存量(如退出战斗后释放预热对象)。数量不足则清到空为止。</summary>
        public static void Remove<T>(int count) where T : class, IReference, new()
        {
            if (count <= 0) return;
            var bucket = GetOrAdd(typeof(T));
            while (count-- > 0 && bucket.Pool.Count > 0)
            {
                bucket.Pool.Dequeue();
                bucket.AtMs.Dequeue();
            }
        }

        /// <summary>清空某类型的全部空闲件(§7.3)。**在途件不受影响**——按契约归还时自然走 Clear;
        /// 被清件只丢弃不走第二次 Clear(与 Trim 同口径)。幂等;未注册类型为 no-op(不建桶)。</summary>
        public static void Clear<T>() where T : class, IReference, new()
        {
            if (s_buckets.TryGetValue(typeof(T), out var b)) { b.Pool.Clear(); b.AtMs.Clear(); }
        }

        /// <summary>清空全部类型的空闲件(重开局/域切换)。在途件不受影响;幂等。</summary>
        public static void ClearAll()
        {
            foreach (var b in s_buckets.Values) { b.Pool.Clear(); b.AtMs.Clear(); }
        }

        /// <summary>按龄收缩（§7.4，指定类型）：销毁该类型桶中空闲时长**超过** maxIdleMs 的件，返回裁剪数。
        /// 调用方驱动、不自动跑；引用池对象无 onDestroy，丢弃即交 GC。未注册类型为 0（不建桶）。</summary>
        public static int TrimExpired<T>(long maxIdleMs) where T : class, IReference, new()
        {
            if (!s_buckets.TryGetValue(typeof(T), out var b)) return 0;
            long now = Environment.TickCount;
            int trimmed = 0;
            while (b.Pool.Count > 0 && now - b.AtMs.Peek() > maxIdleMs)
            {
                b.Pool.Dequeue();
                b.AtMs.Dequeue();
                trimmed++;
            }
            return trimmed;
        }

        /// <summary>按龄收缩（§7.4，跨**全部桶**）：销毁全部桶中空闲超时的件，返回裁剪总数。
        /// 调用方驱动（域切换/低谷期）；时刻为入池时记录的系统钟，仅作收缩启发。</summary>
        public static int TrimExpired(long maxIdleMs)
        {
            long now = Environment.TickCount;
            int trimmed = 0;
            foreach (var b in s_buckets.Values)
            {
                while (b.Pool.Count > 0 && now - b.AtMs.Peek() > maxIdleMs)
                {
                    b.Pool.Dequeue();
                    b.AtMs.Dequeue();
                    trimmed++;
                }
            }
            return trimmed;
        }

        /// <summary>调整某类型的池容量上限(默认 64)。**调小不会立即裁剪已有存量**,只影响后续归还;需要立即释放就配 `Remove`。</summary>
        public static void SetMaxSize<T>(int maxSize) where T : class, IReference, new()
        {
            if (maxSize < 0) throw new ArgumentOutOfRangeException(nameof(maxSize));
            GetOrAdd(typeof(T)).MaxSize = maxSize;
        }

        /// <summary>调整某类型的淘汰策略(默认 DropNewest = 现状;§7.2)。与 <see cref="SetMaxSize{T}"/> 同型的 per-type 配置。</summary>
        public static void SetEviction<T>(PoolEviction eviction) where T : class, IReference, new()
        {
            GetOrAdd(typeof(T)).Eviction = eviction;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
        /// <summary>HUD 调用;每次分配一个列表(HUD 低频轮询,可接受)。</summary>
        public static IReadOnlyList<ReferencePoolInfo> GetAllInfos()
        {
            var list = new List<ReferencePoolInfo>(s_buckets.Count);
            foreach (var kv in s_buckets)
            {
                var b = kv.Value;
                list.Add(new ReferencePoolInfo(kv.Key, b.Pool.Count, b.Using.Count,
                                               b.CreatedCount, b.AcquireCount, b.ReleaseCount,
                                               b.MaxSize, b.PeakUnused, b.DroppedCount,
                                               b.EvictedCount, b.PoolHits));
            }
            return list;
        }
#endif
    }
}
