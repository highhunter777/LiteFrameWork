namespace LiteNet.Protocol
{
    /// <summary>信封分帧的包类型标签（《状态同步实施方案》§4.5 协议矩阵）。
    /// 线格式：[1B PacketType][proto 载荷]——类型分派由 Protocol 层承担，proto 只描述载荷。</summary>
    public enum PacketType : byte
    {
        None = 0,               // 不响应 0x00——过滤随机噪声（同 kcp2k KcpChannel 约定）
        Join = 1,
        JoinAck = 2,
        StartGame = 3,
        Input = 4,
        StateSnapshot = 5,
        MismatchReport = 6,
        Heartbeat = 7,
        Leave = 8,
        ReconnectRequest = 9,
        ReconnectResponse = 10,
        RestoreComplete = 11,     // §9.3 步骤 6：客户端恢复完成 ACK（Reliable）——Restoring → Active 门闩
    }
}
