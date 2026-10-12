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
        Leave = 8,              // 优雅离场（服务端 HandleLeave → Disconnect 命令；客户端发送方随离场批接线）
        ReconnectRequest = 9,
        ReconnectResponse = 10,
        RestoreComplete = 11,     // §9.3 步骤 6：客户端恢复完成 ACK（Reliable）——Restoring → Active 门闩
        MatchEnded = 12,
        StartMatchRequest = 13,   // C→S 房主提前开局（Reliable；开房裁决——owner 判定在 Runtime）
        StartMatchResponse = 14,  // S→C 提前开局反馈（Reliable；仅拒绝时携带原因——成功以 StartGame 广播为准）
        OwnerChanged = 15,        // S→C 房主迁移广播（Reliable；原房主离席 → 最低 playerId 席位继承）
    }
}
