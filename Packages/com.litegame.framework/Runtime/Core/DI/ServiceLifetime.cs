namespace LiteFramework
{
    /// <summary>服务实例的解析生命周期：单例、当前作用域一份、每次解析一份。</summary>
    public enum ServiceLifetime
    {
        Singleton,
        Scoped,
        Transient,
    }
}
