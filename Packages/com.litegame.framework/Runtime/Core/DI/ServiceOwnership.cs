namespace LiteFramework
{
    /// <summary>实例登记的释放责任；Borrowed 保留外部所有者，ContainerOwned 转交同步释放责任。</summary>
    public enum ServiceOwnership
    {
        Borrowed,
        ContainerOwned,
    }
}
