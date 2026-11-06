namespace MetaWire
{
    /// <summary>实例注册响应：<c>{ heartbeatTtlMs }</c>（超过时限未收到心跳即从可分配集合移除）。</summary>
    public sealed class InstanceRegisterResponse
    {
        public long HeartbeatTtlMs { get; set; }
    }
}
