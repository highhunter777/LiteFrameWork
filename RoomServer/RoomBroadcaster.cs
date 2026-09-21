using System;
using System.Collections.Generic;
using LiteNet.Protocol;
using LiteNet.Proto;
using LiteSim;
using Proto = LiteNet.Proto;

namespace RoomServer
{
    /// <summary>
    /// 快照广播器（自 Room 拆出，2026-09-19 高内聚拆分）：**只负责"把权威态发给各客户端"**——
    /// 30Hz 抽帧编排 / SnapshotDiffer 两段式差分 / AOI 可见裁剪 / E1 背压分档与降级 / 全量兜底。
    ///
    /// 职责边界（拆分依据）：Room 管"权威模拟与成员席位"（状态面），本类管"权威态如何到达客户端"
    /// （广播面）——两者的变更原因不同（改手感参数不动广播，改背压策略不动模拟）。
    ///
    /// 依赖：只读输入 = 成员席位数组 + 实体 Id 数组 + 权威态（每步传入）；写 = 每会话的背压/水位记账字段。
    /// 发送出口 = SendTo 委托（ServerHost 装配；测试捕获）——与 Room.SendTo 同款接缝。
    /// </summary>
    public sealed class RoomBroadcaster
    {
        private readonly Session[] _playerSessions;
        private readonly long[] _entityIds;
        private readonly SnapshotDiffer _differ;
        private int _broadcastOrdinal;
        private bool _forceFullPending;

        // ---- Ops 计数（广播面；Room 转发属性保持外部引用兼容）----
        public long SnapshotSent;
        public long SnapshotFullSent;
        public long BackpressureThrottled;

        /// <summary>快照发送出口（ServerHost 装配；测试捕获）。</summary>
        public Action<Session, PacketType, Google.Protobuf.IMessage, bool> SendTo;

        /// <summary>差分器只读暴露（Ops 快照尺寸统计用）。</summary>
        public SnapshotDiffer Differ => _differ;

        public RoomBroadcaster(Session[] playerSessions, long[] entityIds, SnapshotDiffer differ)
        {
            _playerSessions = playerSessions;
            _entityIds = entityIds;
            _differ = differ;
        }

        /// <summary>下一次广播整帧强制全量（重连场景：客户端要从零重建）。</summary>
        public void RequestFullSnapshot() => _forceFullPending = true;

        /// <summary>
        /// 到点广播（30Hz：每 TickRate/SnapshotHz 帧一次）。
        /// E1 背压按会话分档：档位 0 全速；档位 1 抽帧；档位 2 收缩 AOI；档位 3 额外裁掉最远实体。
        /// 降档只影响**该客户端**，其余客户端不受拖累（《服务端架构设计》§10-E1 验收点）。
        /// </summary>
        public void BroadcastIfDue(int frame, SimWorldState authSim, InputGate gate)
        {
            if (frame <= 0) return;
            int stride = SimConfig.TickRate / SimConfig.SnapshotHz;
            if (stride < 1) stride = 1;
            _broadcastOrdinal++;
            if (_broadcastOrdinal % stride != 0) return;

            int broadcastIndex = _broadcastOrdinal / stride;   // 第几次广播（抽帧档按它取模）

            // ① 每广播帧**算一次差分**（推进金标）——多客户端共享同一份，各自只做 AOI 过滤（纯读）。
            // 全量触发（重连待补 / 有客户端 ack 掉队 / 周期性）是**整帧**属性：本帧对所有客户端都是全量，
            // 客户端各自丢弃多余槽位即可（1s 周期兜底本来就会发生，代价可接受；换来的是差分基线的一义性）。
            bool forceFull = _forceFullPending;
            for (int p = 0; p < _playerSessions.Length; p++)
            {
                Session session = _playerSessions[p];
                if (session == null || session.Disconnected) continue;
                if (_differ.NeedsFull(session.LastAckSnapshot)) forceFull = true;
            }
            _forceFullPending = false;
            _differ.BeginFrame(frame, authSim, forceFull);

            // ② 每客户端各取可见部分（背压档位只影响该客户端）
            for (int p = 0; p < _playerSessions.Length; p++)
            {
                Session session = _playerSessions[p];
                if (session == null || session.Disconnected) continue;

                UpdateBackpressureTier(session, frame);
                if (session.BackpressureTier >= 1 && broadcastIndex % ProtocolConstants.ThrottledStride != 0)
                {
                    BackpressureThrottled++;      // 档位 1+：抽帧（该客户端本次不发；其余客户端不受影响）
                    continue;
                }

                SendSnapshot(session, p, frame, authSim, gate);
            }
        }

        private void SendSnapshot(Session session, int playerId, int frame, SimWorldState authSim, InputGate gate)
        {
            long entityId = _entityIds[playerId];
            SimVector3 viewPos = ResolvePosition(authSim, entityId);
            float radius = session.BackpressureTier >= 2 ? ProtocolConstants.ThrottleAoiRadius : SimConfig.AoiRadius;

            // ackInput 口径（2026-09-19 审查修正）：= min(该客户端最新被接受的输入帧, 本快照帧)。
            // 为什么必须钳：inputDelay=1 下"最新接受帧"通常是**服务端帧+1**（客户端发的是未来帧），
            // 直接下发会让客户端回传一个**超前于服务端当前帧**的 AckSnapshot，而 InputGate 收包时
            // 以 `AckSnapshot > serverFrame` 判非法 → **合法输入被自己的 ack 丢掉**（服务器只能用空输入推进）。
            // 钳到本快照帧后：既符合 §3.4.1「ackSnapshot ≤ 服务器已广播帧号」，也保持"输入已到达"的语义。
            int ackInput = Math.Min(gate.LastAcceptedFrame(playerId), frame);
            Proto.StateSnapshot snapshot = _differ.BuildFor(frame, authSim, ackInput, viewPos, radius, entityId);
            if (session.BackpressureTier >= 3) TrimFarthest(snapshot, viewPos);   // 档位 3：低优先级实体丢弃
            if (snapshot.IsFull) SnapshotFullSent++;

            int bytes = snapshot.CalculateSize();
            session.SendQueueBytes += bytes;
            SnapshotSent++;
            if (SendTo != null) SendTo(session, PacketType.StateSnapshot, snapshot, false);
        }

        /// <summary>档位 3：裁掉"距视点最远的"一半实体（低优先级丢弃，§10-E1 第三级）。</summary>
        private static void TrimFarthest(Proto.StateSnapshot snapshot, SimVector3 viewPos)
        {
            if (snapshot.Slots.Count <= 1) return;
            int keep = (int)(snapshot.Slots.Count * ProtocolConstants.ThrottleEntityKeepRatio);
            if (keep >= snapshot.Slots.Count) return;

            // 按 XZ 距离升序（稳定：距离相等时保持原序——确定性），保留前 keep 个
            var pairs = new List<KeyValuePair<float, int>>(snapshot.Slots.Count);
            for (int i = 0; i < snapshot.Slots.Count; i++)
            {
                Proto.SlotDelta d = snapshot.Slots[i];
                float dx = d.PosX - viewPos.X;
                float dz = d.PosZ - viewPos.Z;
                float d2 = SimMath.MulAdd2(dx, dx, dz, dz);
                pairs.Add(new KeyValuePair<float, int>(d2, i));
            }
            pairs.Sort((a, b) => a.Key != b.Key ? a.Key.CompareTo(b.Key) : a.Value.CompareTo(b.Value));

            var kept = new List<Proto.SlotDelta>(keep);
            for (int i = 0; i < keep; i++) kept.Add(snapshot.Slots[pairs[i].Value]);
            snapshot.Slots.Clear();
            snapshot.Slots.AddRange(kept);
        }

        /// <summary>E1 水位判定与档位升降（滞回：水位超限即升档；低于 40% 且持续 2s 才逐档降）。</summary>
        private static void UpdateBackpressureTier(Session session, int frame)
        {
            long queued = session.SendQueueBytes - session.AckedBytes;
            if (queued < 0) queued = 0;

            if (queued > ProtocolConstants.BackpressureQueueLimitBytes)
            {
                if (session.BackpressureTier < 3) session.BackpressureTier++;
                session.BackpressureDrops++;
                session.RecoverSinceFrame = -1;
                return;
            }

            if (session.BackpressureTier > 0)
            {
                if (queued <= ProtocolConstants.BackpressureQueueLimitBytes * ProtocolConstants.BackpressureRecoverRatio)
                {
                    if (session.RecoverSinceFrame < 0) session.RecoverSinceFrame = frame;
                    else if (frame - session.RecoverSinceFrame >= ProtocolConstants.RecoverHoldMillis * SimConfig.TickRate / 1000)
                    {
                        session.BackpressureTier--;
                        session.RecoverSinceFrame = -1;
                    }
                }
                else
                {
                    session.RecoverSinceFrame = -1;
                }
            }
        }

        private static SimVector3 ResolvePosition(SimWorldState authSim, long entityId)
        {
            if (authSim.TryResolve(entityId, out int slot)) return authSim.Entities[slot].Pos;
            return SimVector3.Zero;
        }
    }
}
