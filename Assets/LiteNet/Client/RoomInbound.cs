using System;
using LiteNet.Protocol;

namespace LiteNet
{
    /// <summary>
    /// 入站协议分发（RoomClient 的行内件，M3-6 拆分）：解码 → 按包型落到会话件
    /// （<see cref="RoomSession"/> / <see cref="ReconnectState"/> / <see cref="NetworkQualityProbe"/>），
    /// 再以事件把协议事实交给 RoomClient 的公开订阅面；重连响应裁决与快照水位（帧号单调守卫）也在此收口。
    ///
    /// **边界**：只读协议事实、不持有传输（发包经注入的 send 委托）、不做流程决策；
    /// RoomClient 仍是公共 API 与生命周期的唯一门面（相位/事件与 M2 前逐项一致）。
    /// </summary>
    internal sealed class RoomInbound
    {
        private readonly RoomSession _session;
        private readonly ReconnectState _reconnect;
        private readonly NetworkQualityProbe _probe;
        private readonly Func<long> _nowMs;
        private readonly Action<PacketType, Google.Protobuf.IMessage, bool> _send;

        /// <summary>最近一次收到的输入确认（快照 AckInput 字段；诊断用，不上报）。</summary>
        public int LastAckInput { get; private set; }

        /// <summary>最近一次收到的快照帧号（**单调**：旧包/重复包在入口丢弃）。</summary>
        public int LastSnapshotFrame { get; private set; } = -1;

        public event Action<Proto.JoinAck> JoinAck;
        public event Action<Proto.StartGame> StartGame;
        public event Action<Proto.StateSnapshot> Snapshot;
        public event Action<Proto.CombatEventBatch> CombatEvents;
        public event Action<Proto.MatchEnded> MatchEnded;
        public event Action<Proto.StartMatchResponse> StartMatchResponse;
        public event Action<Proto.OwnerChanged> OwnerChanged;
        public event Action<Proto.RoomSeats> RoomSeats;
        public event Action<Proto.ReconnectResponse> ReconnectResponse;
        public event Action Disconnected;

        /// <param name="send">发包委托（RoomClient 提供——本类不持有传输所有权）。</param>
        public RoomInbound(RoomSession session, ReconnectState reconnect, NetworkQualityProbe probe,
            Func<long> nowMs, Action<PacketType, Google.Protobuf.IMessage, bool> send)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _reconnect = reconnect ?? throw new ArgumentNullException(nameof(reconnect));
            _probe = probe ?? throw new ArgumentNullException(nameof(probe));
            _nowMs = nowMs ?? throw new ArgumentNullException(nameof(nowMs));
            _send = send ?? throw new ArgumentNullException(nameof(send));
        }

        /// <summary>新一次 Join：快照水位回到"未收到"（重连恢复帧可重新推进）。</summary>
        public void ResetSnapshotWatermark() => LastSnapshotFrame = -1;

        /// <summary>传输数据到达：解码失败静默丢弃（坏包不改变会话状态）。</summary>
        public void Handle(ArraySegment<byte> data, bool reliable)
        {
            if (!PacketCodec.TryDecode(data, out var type, out var msg)) return;
            switch (type)
            {
                case PacketType.JoinAck:
                    var ack = (Proto.JoinAck)msg;
                    // 席位/房主/地图/期望人数：JoinAck 权威事实（重连重进同源刷新——迁移后旧主不夺回）
                    _session.ApplyJoinAck(ack);
                    _reconnect.MarkJoinAnswered(ack.ReconnectToken);
                    _reconnect.Transition(ClientSessionPhase.Connected);   // 进房/重新 Join 均以 JoinAck 为准
                    JoinAck?.Invoke(ack);
                    break;
                case PacketType.StartGame:
                    var start = (Proto.StartGame)msg;
                    _reconnect.MarkStartGame(start.Seed, start.ConfigHash);   // §9.3 步骤 2：重连版本确认基准
                    _session.ApplyStartGame(start, _reconnect.JoinAttemptRoomId);   // 缓存载荷：晚订阅者补发
                    StartGame?.Invoke(start);
                    break;
                case PacketType.MatchEnded:
                    if (!reliable) break;
                    var ended = (Proto.MatchEnded)msg;
                    // 终局判据：未开局也接受（等待期房间被服务器中止）——判据退为"本次进房已有应答"；
                    // 重新 Join 期间到达的旧局终局由 TryAcceptMatchEnded 丢弃。
                    if (!_session.TryAcceptMatchEnded(ended, _reconnect.HasStartGame, _reconnect.JoinAttemptPending))
                        break;
                    _reconnect.MarkMatchEnded();
                    MatchEnded?.Invoke(ended.Clone());
                    break;
                case PacketType.StateSnapshot:
                    if (_session.HasMatchResult) break;
                    var snapshot = (Proto.StateSnapshot)msg;
                    // **帧号单调守卫**：快照走 Unreliable，UDP 可乱序；旧包若继续应用，会把 Hp/
                    // 死亡/比分/插值窗口整体回退（表现为血条回血、尸体复活、远端角色倒退）。
                    // 只接收严格新于已应用帧的快照；同帧重复包对镜像无增量语义，一并丢弃。
                    if (snapshot.Frame <= LastSnapshotFrame) break;
                    LastAckInput = snapshot.AckInput;          // 诊断：服务器输入确认（不上报——上报的是快照帧）
                    LastSnapshotFrame = snapshot.Frame;
                    Snapshot?.Invoke(snapshot);
                    break;
                case PacketType.ReconnectResponse:
                    HandleReconnectResponse((Proto.ReconnectResponse)msg);
                    break;
                case PacketType.CombatEvents:
                    // 事件批走 Unreliable——乱序可接受（每条自带权威帧号，消费侧按帧去重/排序），
                    // 但**整批丢弃旧帧**：比已见最新快照帧还旧的批只带已经处理过的事件。
                    CombatEvents?.Invoke((Proto.CombatEventBatch)msg);
                    break;
                case PacketType.StartMatchResponse:                     // 开房裁决：提前开局被拒反馈
                    StartMatchResponse?.Invoke((Proto.StartMatchResponse)msg);
                    break;
                case PacketType.OwnerChanged:                          // 房主迁移：UI 换按钮归属
                    _session.ApplyOwnerChanged(((Proto.OwnerChanged)msg).PlayerId);   // 事实重算（旧主失位/新主到位）
                    OwnerChanged?.Invoke((Proto.OwnerChanged)msg);
                    break;
                case PacketType.Heartbeat:                              // RTT/丢包探针回声（服务器原样回显 client_time）
                    _probe.ObserveEcho(((Proto.Heartbeat)msg).ClientTime, _nowMs());
                    break;
                case PacketType.RoomSeats:                              // 席位实时面：房间页玩家列表全量快照
                    _session.ApplySeats((Proto.RoomSeats)msg);          // 入缓 Clone（读侧再 Clone——同 MatchResult 防御）
                    RoomSeats?.Invoke((Proto.RoomSeats)msg);
                    break;
            }
        }

        /// <summary>重拨连接建立（Reconnecting 中）：自动发 ReconnectRequest；连接等待窗重置（握手耗时不再挤占响应预算）。</summary>
        public void OnTransportConnected()
        {
            if (!_reconnect.OnTransportConnected()) return;
            _send(PacketType.ReconnectRequest, new Proto.ReconnectRequest { OneTimeToken = _reconnect.Token }, true);
        }

        /// <summary>传输断开：在房/恢复中 → SuspectedLost（席位仍在重连窗口内）；Reconnecting 保持（超时线裁决）。</summary>
        public void OnTransportDisconnected(string contentContext)
        {
            // 诊断（§8「可诊断」）：发了 Join 又没等到 JoinAck 就断开——服务器侧拒绝（未回原因，见
            // ServerHost.RejectJoin）的**客户端侧事实**；与服务器记录同键，两端据此对齐。
            if (_reconnect.JoinAttemptPending && _reconnect.Phase == ClientSessionPhase.Idle)
            {
                _reconnect.MarkJoinUnanswered();
                RoomClientDiagnostics.JoinNoAckDisconnected(
                    _reconnect.JoinAttemptRoomId, _session.PlayerId, _reconnect.BuildHash, contentContext);
            }
            if (!_session.HasMatchResult
                && (_reconnect.Phase == ClientSessionPhase.Connected || _reconnect.Phase == ClientSessionPhase.Restoring))
                _reconnect.Transition(ClientSessionPhase.SuspectedLost);
            Disconnected?.Invoke();
        }

        /// <summary>
        /// 重连响应（§9.3 步骤 2~4）：Ok 且版本确认一致 → Restoring（应用层应用快照/历史后
        /// <see cref="RoomClient.CompleteRestore"/>）；Ok 但版本不符 → Failed（绝不发恢复 ACK——不是同一局）；
        /// 拒绝 → Failed。响应照常派发（应用层据相位判定是否可应用）。
        /// </summary>
        private void HandleReconnectResponse(Proto.ReconnectResponse response)
        {
            if (_reconnect.Phase == ClientSessionPhase.Reconnecting)
            {
                bool ok = response.Ok && _reconnect.VersionMatches(response.Seed, response.ConfigHash, response.BuildHash);
                _reconnect.Transition(ok ? ClientSessionPhase.Restoring : ClientSessionPhase.Failed);
            }
            // 恢复快照推进快照帧水位（增量广播从该帧之后继续；陈旧快照守卫不会被恢复帧误伤）。
            if (response.Ok && response.Snapshot != null && response.Snapshot.Frame > LastSnapshotFrame)
                LastSnapshotFrame = response.Snapshot.Frame;
            ReconnectResponse?.Invoke(response);
        }
    }
}
