namespace LiteFramework
{
    /// <summary>客户端宿主生命周期状态（数值与旧 int 语义一致，便于日志/遥测连续）。</summary>
    public enum HostState
    {
        Created = 0,
        Initializing = 1,
        Running = 2,
        ShuttingDown = 3,
        Closed = 4,
    }
}
