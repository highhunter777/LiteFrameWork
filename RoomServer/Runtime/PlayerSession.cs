namespace RoomServer.Runtime
{
    /// <summary>席位阶段（§9.2：PlayerSession 在重连窗口内保留）。</summary>
    public enum SeatPhase
    {
        /// <summary>已绑定活跃连接，输入/增量广播正常。</summary>
        Active = 0,
        /// <summary>连接已断、席位保留（重连窗口内可重绑；权威循环对该席位沿用空输入）。</summary>
        Disconnected,
        /// <summary>重连恢复中：已验票重绑（R1 批③ 落地），等待客户端恢复完成 ACK——此期间 App 管线抑制该席位增量广播。</summary>
        Restoring,
    }

    /// <summary>
    /// 玩家席位（§9.2 Connection 与 PlayerSession 分离的席位侧）：
    /// **与传输连接解耦**——连接断了席位仍在（重连窗口内），playerId/entityId 全程不变
    /// （§5.6 权威快照恢复的前提）。背压/ACK/发送 ledger 等**连接级**状态不在本类（归 App 层会话）。
    ///
    /// 输入去重水位（<see cref="InputGate"/> 的 _lastAcceptedFrame/_lastActionSeq）随席位保留：
    /// 重连后旧帧不会被重复接受，也不需要重置（§9.2"connectionId 复用不能复活旧身份"的席位侧保证）。
    /// </summary>
    public sealed class PlayerSession
    {
        /// <summary>房间内席位号（从 0 递增，与 Sim 输入槽/实体表下标一致——定容不移位）。</summary>
        public readonly int PlayerId;

        /// <summary>玩家实体 Id（StartMatch 分配；重连/重绑不变）。</summary>
        public long EntityId;

        public SeatPhase Phase;

        /// <summary>当前绑定的传输连接 Id（-1 = 未绑定，如已在重连窗口内等待）。</summary>
        public int ConnectionId = -1;

        /// <summary>重绑代数（每次 Rebind 递增；用于拒绝旧连接的迟到命令——R1 重连批启用校验）。</summary>
        public int RebindGeneration;

        public PlayerSession(int playerId, int connectionId)
        {
            PlayerId = playerId;
            ConnectionId = connectionId;
            Phase = SeatPhase.Active;
        }
    }
}
