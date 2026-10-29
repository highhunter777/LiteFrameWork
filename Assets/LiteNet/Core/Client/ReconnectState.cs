using System;

namespace LiteNet
{
    /// <summary>
    /// 会话相位与重连凭据（RoomClient 的行内件，M2 拆分）：相位迁移、一次性票据、版本确认基准
    /// 与重连等待线。**不做 I/O、不碰传输**——重拨/发包仍由 <see cref="RoomClient"/> 编排。
    ///
    /// 状态机（§9.3）：Connected → SuspectedLost → Reconnecting → Restoring → Connected；
    /// Reconnecting 超时/被拒 → Failed（应用层决策，不自动重试）。
    /// </summary>
    internal sealed class ReconnectState
    {
        private readonly Func<long> _nowMs;
        private readonly long _timeoutMs;

        private ClientSessionPhase _phase = ClientSessionPhase.Idle;
        private string _token;                 // JoinAck 下发的一次性票据（断线重连凭据）
        private string _buildHash;             // 本端 Join 时发送的构建哈希（版本确认比对基准）
        private string _joinAttemptRoomId;     // 最近一次进房尝试的目标房间（诊断记录用）
        private bool _joinAttemptPending;      // 已发 Join 未收 JoinAck（诊断：断线记录据此判定"未获应答"）
        private bool _hasStartGame;            // StartGame 已达（版本确认的 seed/配置基准）
        private long _matchSeed;
        private uint _matchConfigHash;
        private long _deadlineMs;              // Reconnecting 相位的超时线（注入时钟口径）

        /// <summary>相位迁移通知（从/到；Failed 原因见响应或超时事实本身）。</summary>
        public event Action<ClientSessionPhase, ClientSessionPhase> Changed;

        public ReconnectState(Func<long> nowMs, long timeoutMs)
        {
            _nowMs = nowMs ?? throw new ArgumentNullException(nameof(nowMs));
            _timeoutMs = timeoutMs > 0 ? timeoutMs : RoomClient.ReconnectTimeoutMs;
        }

        public ClientSessionPhase Phase => _phase;
        public string Token => _token;
        public string BuildHash => _buildHash;
        public string JoinAttemptRoomId => _joinAttemptRoomId;
        public bool JoinAttemptPending => _joinAttemptPending;
        public bool HasStartGame => _hasStartGame;
        public long MatchSeed => _matchSeed;
        public uint MatchConfigHash => _matchConfigHash;
        public string LastHost { get; private set; }
        public int LastPort { get; private set; }

        /// <summary>记住连接端点（重拨用）。</summary>
        public void RememberEndpoint(string host, int port)
        {
            LastHost = host;
            LastPort = port;
        }

        /// <summary>发 Join 前重置本局身份（新一次进房）。</summary>
        public void ResetMatchIdentity()
        {
            _hasStartGame = false;
            _matchSeed = 0;
            _matchConfigHash = 0;
        }

        /// <summary>记录一次 Join 已发出（等待应答）。</summary>
        public void MarkJoinSent(string roomId, string buildHash)
        {
            _buildHash = buildHash;                 // 版本确认基准（§9.3 步骤 2：重连响应须同值）
            _joinAttemptRoomId = roomId;
            _joinAttemptPending = true;
        }

        /// <summary>Join 未获应答即断开（诊断判定用）：清未决标记。</summary>
        public void MarkJoinUnanswered() => _joinAttemptPending = false;

        /// <summary>JoinAck 到达：登记一次性票据、清未决标记。</summary>
        public void MarkJoinAnswered(string reconnectToken)
        {
            _joinAttemptPending = false;
            _token = string.IsNullOrEmpty(reconnectToken) ? null : reconnectToken;
        }

        /// <summary>StartGame 到达：登记版本确认基准。</summary>
        public void MarkStartGame(long seed, uint configHash)
        {
            _matchSeed = seed;
            _matchConfigHash = configHash;
            _hasStartGame = true;
        }

        /// <summary>终局到达：票据一次性，随局作废（重连窗口关闭）。</summary>
        public void MarkMatchEnded() => _token = null;

        /// <summary>
        /// 发起重连（SuspectedLost → Reconnecting）。返回 false = 未发起（相位不符，或无票据 →
        /// 已转 Failed——票据耗尽后唯一出路是重新 Join，由应用层决策）。
        /// </summary>
        public bool BeginReconnect(out string host, out int port)
        {
            host = LastHost;
            port = LastPort;
            if (_phase != ClientSessionPhase.SuspectedLost) return false;

            if (string.IsNullOrEmpty(_token))
            {
                Transition(ClientSessionPhase.Failed);   // 无票据：重连窗口已耗尽/从未进房
                return false;
            }

            Transition(ClientSessionPhase.Reconnecting);
            _deadlineMs = _nowMs() + _timeoutMs;
            return true;
        }

        /// <summary>重拨连接建立（Reconnecting 中）：连接等待窗重置（握手耗时不再挤占响应预算）。</summary>
        public bool OnTransportConnected()
        {
            if (_phase != ClientSessionPhase.Reconnecting) return false;
            _deadlineMs = _nowMs() + _timeoutMs;
            return true;
        }

        /// <summary>宣告恢复完成（Restoring → Connected）。返回 false = 相位不符。</summary>
        public bool CompleteRestore()
        {
            if (_phase != ClientSessionPhase.Restoring) return false;
            Transition(ClientSessionPhase.Connected);
            return true;
        }

        /// <summary>Reconnecting 超时巡检（§9.3"超时 → Failed"；Tick 驱动——无独立线程/定时器）。</summary>
        public void CheckDeadline()
        {
            if (_phase != ClientSessionPhase.Reconnecting) return;
            if (_nowMs() < _deadlineMs) return;
            Transition(ClientSessionPhase.Failed);
        }

        /// <summary>版本确认（§9.3 步骤 2）：seed/配置摘要/构建哈希须与 StartGame/Join 事实一致——跨版本混房即失败。</summary>
        public bool VersionMatches(long seed, uint configHash, string buildHash)
        {
            if (_hasStartGame && (seed != _matchSeed || configHash != _matchConfigHash)) return false;
            if (_buildHash != null && buildHash != _buildHash) return false;
            return true;
        }

        /// <summary>相位迁移唯一入口（非法路径不静默改态——各调用点前置校验）。</summary>
        public void Transition(ClientSessionPhase to)
        {
            if (_phase == to) return;
            ClientSessionPhase from = _phase;
            _phase = to;
            Changed?.Invoke(from, to);
        }
    }
}
