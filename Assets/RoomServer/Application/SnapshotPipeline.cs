using System;
using System.Collections.Generic;
using LiteNet.Protocol;
using LiteNet.Proto;
using LiteSim;
using RoomServer.Runtime;
using Proto = LiteNet.Proto;

namespace RoomServer.Application
{
    /// <summary>
    /// 快照广播管线（R1：自 <c>RoomBroadcaster</c> 迁入 App 层——**只负责"把权威态发给各客户端"**：
    /// 30Hz 抽帧编排 / SnapshotDiffer 两段式差分 / AOI 可见裁剪 / E1 背压分档与降级 / 全量兜底）。
    ///
    /// 归属裁定（R1 契约）：差分器 <see cref="SnapshotDiffer"/> 与编码器 <see cref="SnapshotCodec"/>
    /// 消费 proto 且是快照格式的**唯一单源**（§19 禁第二套快照 DTO）——因此快照构建整体留在 App 层，
    /// RoomRuntime 只持权威态（<see cref="RoomRuntime.AuthSim"/>）与输入闸门（<see cref="RoomRuntime.Gate"/>），
    /// 由本管线在每次权威步进后**拉取**构建下发。Runtime 不产快照输出，两层无需快照契约。
    ///
    /// 依赖：只读输入 = 席位→会话数组 + 实体 Id 解析（<see cref="RoomRuntime.EntityIdOf"/>）+ 权威态；
    /// 写 = 每会话的背压/水位记账字段（App 层会话）。
    /// </summary>
    public sealed class SnapshotPipeline
    {
        private readonly Session[] _seatSessions;
        private readonly SnapshotDiffer _differ;
        private int _broadcastOrdinal;
        private bool _forceFullPending;

        // ---- Ops 计数（广播面）----
        public long SnapshotSent;
        public long SnapshotFullSent;
        public long BackpressureThrottled;

        /// <summary>快照发送出口（ServerHost 装配；测试捕获）。</summary>
        public Action<Session, PacketType, Google.Protobuf.IMessage, bool> SendTo;

        /// <summary>差分器只读暴露（Ops 快照尺寸统计用）。</summary>
        public SnapshotDiffer Differ => _differ;

        public SnapshotPipeline(Session[] seatSessions, SnapshotDiffer differ = null)
        {
            _seatSessions = seatSessions;
            _differ = differ ?? new SnapshotDiffer();
        }

        /// <summary>
        /// 下一次广播整帧强制全量。**唯一生产调用点 = 重连恢复完成**（SeatRestored）：
        /// 抑制期间差分基线已推进到 B（> 重连响应快照帧 N_r），恢复后若直接投增量会留
        /// (N_r, B] 陈旧缝——整帧全量把所有人的广播链重新锚定到同一帧，增量自此无缺口。
        /// （全量是整帧属性：其他客户端多付一帧全量，换来差分基线一义性。）
        /// </summary>
        public void RequestFullSnapshot() => _forceFullPending = true;

        /// <summary>
        /// 到点广播（30Hz：每 TickRate/SnapshotHz 帧一次）。
        /// E1 背压按会话分档：档位 0 全速；档位 1 抽帧；档位 2 收缩 AOI；档位 3 额外裁掉最远实体。
        /// 降档只影响**该客户端**，其余客户端不受拖累（《服务端架构设计》§10-E1 验收点）。
        ///
        /// <paramref name="seatBroadcastable"/>（playerId → 席位是否可播）：null = 不抑制；
        /// ServerHost 传"席位 == Active"——Restoring 席位（重连恢复中，§9.3 步骤 6）在**本方法两个环**都被跳过：
        /// 发送环不向其投增量；NeedsFull 扫描环也不看它（其 fresh 水位 ack=-1 若参与判定，会把
        /// **整帧**持续强制成全量——其他客户端白白付全量带宽）。恢复完成由宿主经
        /// <see cref="RequestFullSnapshot"/> 显式重锚整帧。
        /// </summary>
        public void BroadcastIfDue(int frame, SimWorldState authSim, InputGate gate, Func<int, long> entityIdOf,
            Func<int, bool> seatBroadcastable = null)
        {
            if (frame <= 0) return;
            int stride = SimConfig.TickRate / SimConfig.SnapshotHz;
            if (stride < 1) stride = 1;
            _broadcastOrdinal++;
            if (_broadcastOrdinal % stride != 0) return;

            int broadcastIndex = _broadcastOrdinal / stride;   // 第几次广播（抽帧档按它取模）

            bool Suppressed(int p) => seatBroadcastable != null && !seatBroadcastable(p);

            // ① 每广播帧**算一次差分**（推进金标）——多客户端共享同一份，各自只做 AOI 过滤（纯读）。
            // 全量触发（显式请求 / 有客户端 ack 掉队 / 周期性）是**整帧**属性：本帧对所有客户端都是全量，
            // 客户端各自丢弃多余槽位即可（1s 周期兜底本来就会发生，代价可接受；换来的是差分基线的一义性）。
            bool forceFull = _forceFullPending;
            for (int p = 0; p < _seatSessions.Length; p++)
            {
                Session session = _seatSessions[p];
                if (session == null || session.Disconnected || Suppressed(p)) continue;
                if (_differ.NeedsFull(session.LastAckSnapshot)) forceFull = true;
            }
            _forceFullPending = false;
            _differ.BeginFrame(frame, authSim, forceFull);

            // ② 每客户端各取可见部分（背压档位只影响该客户端；Restoring 席位抑制——§9.3 步骤 6）
            for (int p = 0; p < _seatSessions.Length; p++)
            {
                Session session = _seatSessions[p];
                if (session == null || session.Disconnected || Suppressed(p)) continue;

                UpdateBackpressureTier(session, frame);
                if (session.BackpressureTier >= 1 && broadcastIndex % ProtocolConstants.ThrottledStride != 0)
                {
                    BackpressureThrottled++;      // 档位 1+：抽帧（该客户端本次不发；其余客户端不受影响）
                    continue;
                }

                SendSnapshot(session, p, frame, authSim, gate, entityIdOf);
            }
        }

        private void SendSnapshot(Session session, int playerId, int frame, SimWorldState authSim, InputGate gate,
            Func<int, long> entityIdOf)
        {
            long entityId = entityIdOf(playerId);
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
            session.RecordSnapshotSend(frame, bytes);      // R0-P0-4：发送 ledger 记账（ACK 释放的唯一依据）
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
