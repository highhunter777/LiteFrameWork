namespace LiteFramework
{
    /// <summary>
    /// 引用池端口（实例生命周期）。主线程 only；与 <see cref="ReferencePoolService"/> 的
    /// per-type 桶/淘汰/统计语义一致。静态 <see cref="ReferencePool"/> 只是兼容门面。
    /// </summary>
    public interface IReferencePool
    {
        T Acquire<T>() where T : class, IReference, new();
        void Release(IReference reference);
        void Add<T>(int count) where T : class, IReference, new();
        void Remove<T>(int count) where T : class, IReference, new();
        void Clear<T>() where T : class, IReference, new();
        void ClearAll();
        int TrimExpired<T>(long maxIdleMs) where T : class, IReference, new();
        int TrimExpired(long maxIdleMs);
        void SetMaxSize<T>(int maxSize) where T : class, IReference, new();
        void SetEviction<T>(PoolEviction eviction) where T : class, IReference, new();
    }
}
