using System;
using System.Collections.Generic;
using LiteNet.Protocol;
using LiteSync.Shared;
using LiteSync.Snapshot;
using LiteNet.Proto;
using LiteSim;
using RoomServer.Runtime;
using Proto = LiteNet.Proto;

namespace RoomServer.Application
{
    /// <summary>
    /// 快照广播管线（R1：自 <c>RoomBroadcaster</c> 迁入 App 层——**只负责"把权威态发给各客户端"**： 30Hz 抽帧编排 / SnapshotDiffer 两段式差分 / AOI 可见裁剪 。）。
    /// </summary>
    public sealed class SnapshotPipeline
    {
        private readonly IBroadcastSeat[] _seats;
        private readonly SnapshotDiffer _differ;
        private readonly AoiSettings _aoi;          // 房级 AOI 参数（基础/节流半径都由它派生）
        private readonly int _snapshotHz;           // 房级快照频率（stride = TickRate / 本值）
        private int _broadcastOrdinal;
        private bool _forceFullPending;

        // ---- Ops 计数（广播面）----
        public long SnapshotSent;
        public long SnapshotFullSent;
        public long BackpressureThrottled;

        /// <summary>**因超不可靠预算被放弃的快照数**（2026-10-18 批新增）。
        /// 旧实现是"先 <c>SnapshotSent++</c> 再 <c>SendTo</c>"——kcp2k 丢弃时 Ops 仍然算它发过，
        /// 于是这次事故在监控上完全隐形（`sent=13824` 全虚高）。本计数是那条链路的真话：
        /// **长期非 0 = 有人把快照撑爆了**，立刻查槽位数与新增字段。</summary>
        public long SnapshotDropped;

        /// <summary>最近一份快照的 proto 载荷字节（Ops 观测：与 <see cref="WireBudget.ProtoPayload"/> 的余量）。</summary>
        public int LastSnapshotBytes;

        /// <summary>最近一份快照的 wire 字节（含信封与类型字节——真正的线上尺寸）。</summary>
        public int LastSnapshotWireBytes;

        /// <summary>上一次 <see cref="BroadcastIfDue"/> 是否真的广播了（= 命中 30Hz 抽帧节拍）。
        /// 事件面据此与状态面**严格同节拍**（<see cref="CombatEventPipeline.Publish"/> 只在为 true 时调）。</summary>
        public bool BroadcastedThisFrame;

        /// <summary>快照发送出口（装配方注入：宿主 owner 形态直发会话；Worker 执行形态入 Outbound 回传）。
        /// 载荷为席位视图——发送方按 <see cref="IBroadcastSeat.ConnectionId"/> / 代次定位连接。</summary>
        public Action<IBroadcastSeat, PacketType, Google.Protobuf.IMessage, bool> SendTo;

        /// <summary>差分器只读暴露（Ops 快照尺寸统计用）。</summary>
        public SnapshotDiffer Differ => _differ;

        /// <param name="differ">差分器（null = 按本管线的 <paramref name="aoi"/> 新建——每房一份网格）。</param>
        /// <param name="aoi">房级 AOI 参数（null = 缺省 <see cref="AoiSettings.Default"/>）。</param>
        /// <param name="snapshotHz">房级快照频率（Hz；范围 1..120，越界抛）。</param>
        public SnapshotPipeline(IBroadcastSeat[] seats, SnapshotDiffer differ = null, AoiSettings aoi = null,
            int snapshotHz = ProtocolConstants.DefaultSnapshotHz)
        {
            if (snapshotHz < ProtocolConstants.MinSnapshotHz || snapshotHz > ProtocolConstants.MaxSnapshotHz)
                throw new ArgumentOutOfRangeException(nameof(snapshotHz), snapshotHz,
                    $"快照频率越界（允许 {ProtocolConstants.MinSnapshotHz}..{ProtocolConstants.MaxSnapshotHz}）");

            _seats = seats;
            _aoi = aoi ?? AoiSettings.Default;
            _snapshotHz = snapshotHz;
            _differ = differ ?? new SnapshotDiffer(_aoi);
        }

        /// <summary>
        /// 下一次广播整帧强制全量。
        /// </summary>
        public void RequestFullSnapshot() => _forceFullPending = true;

        /// <summary>
        /// 到点广播（30Hz：每 TickRate/SnapshotHz 帧一次）。
        /// </summary>
        public void BroadcastIfDue(int frame, SimWorldState authSim, InputGate gate, Func<int, long> entityIdOf,
            Func<int, bool> seatBroadcastable = null)
        {
            BroadcastedThisFrame = false;
            if (frame <= 0) return;
            int stride = SimConfig.TickRate / _snapshotHz;
            if (stride < 1) stride = 1;
            _broadcastOrdinal++;
            if (_broadcastOrdinal % stride != 0) return;
            BroadcastedThisFrame = true;

            int broadcastIndex = _broadcastOrdinal / stride;   // 第几次广播（抽帧档按它取模）

            bool Suppressed(int p) => seatBroadcastable != null && !seatBroadcastable(p);

            // ① 每广播帧**算一次差分**（推进金标）——多客户端共享同一份，各自只做 AOI 过滤（纯读）。
            bool forceFull = _forceFullPending;
            for (int p = 0; p < _seats.Length; p++)
            {
                IBroadcastSeat seat = _seats[p];
                if (seat == null || seat.Disconnected || Suppressed(p)) continue;
                if (_differ.NeedsFull(seat.Account.LastAckSnapshot)) forceFull = true;
            }
            _forceFullPending = false;
            // 事件面已独立成包（CombatEventPipeline），此处不再灌 MatchStateDelta 的公共窗口。
            _differ.BeginFrame(frame, authSim, forceFull);

            // ② 每客户端各取可见部分（背压档位只影响该客户端；Restoring 席位抑制——§9.3 步骤 6）
            for (int p = 0; p < _seats.Length; p++)
            {
                IBroadcastSeat seat = _seats[p];
                if (seat == null || seat.Disconnected || Suppressed(p)) continue;

                UpdateBackpressureTier(seat.Account, frame);
                if (seat.Account.BackpressureTier >= 1 && broadcastIndex % ProtocolConstants.ThrottledStride != 0)
                {
                    BackpressureThrottled++;      // 档位 1+：抽帧（该客户端本次不发；其余客户端不受影响）
                    continue;
                }

                SendSnapshot(seat, p, frame, authSim, gate, entityIdOf);
            }
        }

        private void SendSnapshot(IBroadcastSeat seat, int playerId, int frame, SimWorldState authSim, InputGate gate,
            Func<int, long> entityIdOf)
        {
            BroadcastAccount account = seat.Account;
            long entityId = entityIdOf(playerId);
            SimVector3 viewPos = ResolvePosition(authSim, entityId);
            float radius = account.BackpressureTier >= 2 ? _aoi.ThrottleRadius : _aoi.Radius;

            // ackInput 口径（2026-09-19 审查修正）：= min(该客户端最新被接受的输入帧, 本快照帧)。
            int ackInput = Math.Min(gate.LastAcceptedFrame(playerId), frame);
            Proto.StateSnapshot snapshot = _differ.BuildFor(frame, authSim, ackInput, viewPos, radius, entityId);
            if (account.BackpressureTier >= 3) TrimFarthest(snapshot, viewPos);   // 档位 3：低优先级实体丢弃（仅增量帧）
            if (snapshot.IsFull) SnapshotFullSent++;

            // ---- 预算守卫（2026-10-18 批）：**发送前**判定，永不让 kcp2k 替我们静默丢 ---- 旧实现的教训：SnapshotSent++ 写在 SendTo 之前，包被 kcp2k 丢掉后 Ops 仍报"已发"， 故障在监控上完全隐形。
            int protoBytes = snapshot.CalculateSize();
            LastSnapshotBytes = protoBytes;
            LastSnapshotWireBytes = protoBytes + WireBudget.EnvelopeBytes + WireBudget.TypeBytes;

            if (WireBudget.RequiresFallback(protoBytes))
            {
                // 降级阶梯：① 先裁到 AOI 最远的一半（仅增量帧；全量帧的"缺席判死"不允许裁）→ ② 仍超则放弃本席位本帧。
                // 状态快照**没有可裁的事件面**了（已迁出），所以能做的只有"缩范围"或"诚实放弃并计数"。
                if (!snapshot.IsFull) TrimFarthest(snapshot, viewPos);
                protoBytes = snapshot.CalculateSize();
                LastSnapshotBytes = protoBytes;
                LastSnapshotWireBytes = protoBytes + WireBudget.EnvelopeBytes + WireBudget.TypeBytes;
            }

            if (WireBudget.RequiresFallback(protoBytes))
            {
                SnapshotDropped++;                 // 留痕：长期非 0 = 有人把快照撑爆了（查槽位数/新增字段）
                return;                            // 不发——比发一个必被丢弃的包诚实（不虚增 SnapshotSent）
            }

            account.RecordSnapshotSend(frame, protoBytes);   // R0-P0-4：发送 ledger 记账（ACK 释放的唯一依据）
            SnapshotSent++;
            if (SendTo != null) SendTo(seat, PacketType.StateSnapshot, snapshot, false);
        }

        /// <summary>
        /// 档位 3：裁掉"距视点最远的"一半实体（低优先级丢弃，§10-E1 第三级）。
        /// </summary>
        private static void TrimFarthest(Proto.StateSnapshot snapshot, SimVector3 viewPos)
        {
            if (snapshot.IsFull) return;           // 全量整帧保留（缺席判死只在全图全量下成立）
            if (snapshot.Slots.Count <= 1) return;
            int keep = (int)(snapshot.Slots.Count * ProtocolConstants.ThrottleEntityKeepRatio);
            if (keep >= snapshot.Slots.Count) return;
            if (keep < 1) keep = 1;                // 至少留自己（否则客户端连自己都没有）

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
        private static void UpdateBackpressureTier(BroadcastAccount account, int frame)
        {
            long queued = account.SendQueueBytes - account.AckedBytes;
            if (queued < 0) queued = 0;

            if (queued > ProtocolConstants.BackpressureQueueLimitBytes)
            {
                // 升档是一个完整事件（档位+1 封顶 / 降级计数 / 退出恢复观察）——写面收在 BroadcastAccount 内
                account.RaiseBackpressureTier();
                return;
            }

            if (account.BackpressureTier > 0)
            {
                if (queued <= ProtocolConstants.BackpressureQueueLimitBytes * ProtocolConstants.BackpressureRecoverRatio)
                {
                    if (account.RecoverSinceFrame < 0) account.MarkRecoverObservation(frame);
                    else if (frame - account.RecoverSinceFrame >= ProtocolConstants.RecoverHoldMillis * SimConfig.TickRate / 1000)
                        account.LowerBackpressureTier();
                }
                else
                {
                    account.ClearRecoverObservation();
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
