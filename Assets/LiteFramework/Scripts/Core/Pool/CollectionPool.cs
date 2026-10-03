using System.Collections.Generic;

namespace LiteFramework
{
    /// <summary>
    /// 集合池内核（《对象池专项设计》§4）：组合 <see cref="ObjectPool{T}"/>，不新写池逻辑——
    /// 高频临时集合（每帧/每广播的 List/Dictionary/HashSet）的借还走与实例池同一套
    /// 所有权移交/重复归还检测/容量销毁/统计机制；onRelease = Clear()（归还即自清，防陈旧数据回带）。
    /// 借用方须 Release 同一实例（所有权移交制同内核）；既有"调用方持实例 Clear 复用"的调用点不强制迁移。
    /// </summary>
    public sealed class CollectionPool<TCollection, TItem> where TCollection : class, ICollection<TItem>, new()
    {
        private readonly ObjectPool<TCollection> _pool;

        public CollectionPool(int maxIdle = 8, string statsName = null)
        {
            _pool = new ObjectPool<TCollection>(
                create: () => new TCollection(),
                onRelease: c => c.Clear(),
                maxIdle: maxIdle,
                statsName: statsName ?? $"CollectionPool.{typeof(TCollection).Name}");
        }

        /// <summary>取一个已清空的集合（状态未定义契约同内核：内容由调用方填充）。</summary>
        public TCollection Acquire() => _pool.Acquire();

        /// <summary>归还（Clear 由池执行）。nowMs 语义同内核（供按龄收缩，可选）。</summary>
        public void Release(TCollection collection, long nowMs = 0) => _pool.Release(collection, nowMs);

        public int MaxIdle => _pool.MaxIdle;
        public int UnusedCount => _pool.UnusedCount;
        public long AcquiredCount => _pool.AcquiredCount;
        public long PoolHits => _pool.PoolHits;
    }

    /// <summary>临时 List 池静态门面：Get/Release 同实例（每闭合类型一组桶状态，静态天然按 T 分桶）。
    /// maxIdle 默认 8（集合池典型小容量）。</summary>
    public static class ListPool<T>
    {
        private static readonly CollectionPool<List<T>, T> s_pool = new CollectionPool<List<T>, T>();

        public static List<T> Get() => s_pool.Acquire();
        public static void Release(List<T> list) => s_pool.Release(list);
        public static int UnusedCount => s_pool.UnusedCount;
    }

    /// <summary>临时 Dictionary 池静态门面（TItem = KeyValuePair——Dictionary 实现 ICollection&lt;KeyValuePair&lt;K,V&gt;&gt;）。</summary>
    public static class DictionaryPool<K, V>
    {
        private static readonly CollectionPool<Dictionary<K, V>, KeyValuePair<K, V>> s_pool =
            new CollectionPool<Dictionary<K, V>, KeyValuePair<K, V>>();

        public static Dictionary<K, V> Get() => s_pool.Acquire();
        public static void Release(Dictionary<K, V> dict) => s_pool.Release(dict);
        public static int UnusedCount => s_pool.UnusedCount;
    }

    /// <summary>临时 HashSet 池静态门面。</summary>
    public static class HashSetPool<T>
    {
        private static readonly CollectionPool<HashSet<T>, T> s_pool = new CollectionPool<HashSet<T>, T>();

        public static HashSet<T> Get() => s_pool.Acquire();
        public static void Release(HashSet<T> set) => s_pool.Release(set);
        public static int UnusedCount => s_pool.UnusedCount;
    }
}
