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
    /// **2026-10-18 批：快照回归纯状态**。原先本类还要往 <c>MatchStateDelta</c> 里灌三个公共事件窗口
    /// （击杀/开火/命中），实测把快照顶到 1531B &gt; KCP 不可靠上限 1194B ⇒ 整份快照被静默丢弃
    /// ⇒ 状态流中断（联机"打一会儿就不同步"的真因）。事件面已迁到 <see cref="CombatEventPipeline"/>
    /// 的独立包；本类只剩状态（slots + 比赛规则字段 + 本人私有面），尺寸只随实体数变。
    ///
    /// 依赖：只读输入 = 席位→广播视图数组（<see cref="IBroadcastSeat"/>：连接号/代次/断开标记/记账）
    /// + 实体 Id 解析（<see cref="RoomRuntime.EntityIdOf"/>）+ 权威态；
    /// 写 = 每席位的广播记账（<see cref="BroadcastAccount"/>——宿主 owner 形态挂在会话上，
    /// Worker 执行形态挂在房间本地席位槽上）。
    /// </summary>
    public sealed class SnapshotPipeline
    {
        private readonly IBroadcastSeat[] _seats;
        private readonly SnapshotDiffer _differ;
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

        public SnapshotPipeline(IBroadcastSeat[] seats, SnapshotDiffer differ = null)
        {
            _seats = seats;
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
            BroadcastedThisFrame = false;
            if (frame <= 0) return;
            int stride = SimConfig.TickRate / SimConfig.SnapshotHz;
            if (stride < 1) stride = 1;
            _broadcastOrdinal++;
            if (_broadcastOrdinal % stride != 0) return;
            BroadcastedThisFrame = true;

            int broadcastIndex = _broadcastOrdinal / stride;   // 第几次广播（抽帧档按它取模）

            bool Suppressed(int p) => seatBroadcastable != null && !seatBroadcastable(p);

            // ① 每广播帧**算一次差分**（推进金标）——多客户端共享同一份，各自只做 AOI 过滤（纯读）。
            // 全量触发（显式请求 / 有客户端 ack 掉队 / 周期性）是**整帧**属性：本帧对所有客户端都是全量，
            // 客户端各自丢弃多余槽位即可（1s 周期兜底本来就会发生，代价可接受；换来的是差分基线的一义性）。
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
            float radius = account.BackpressureTier >= 2 ? ProtocolConstants.ThrottleAoiRadius : SimConfig.AoiRadius;

            // ackInput 口径（2026-09-19 审查修正）：= min(该客户端最新被接受的输入帧, 本快照帧)。
            // 为什么必须钳：inputDelay=1 下"最新接受帧"通常是**服务端帧+1**（客户端发的是未来帧），
            // 直接下发会让客户端回传一个**超前于服务端当前帧**的 AckSnapshot，而 InputGate 收包时
            // 以 `AckSnapshot > serverFrame` 判非法 → **合法输入被自己的 ack 丢掉**（服务器只能用空输入推进）。
            // 钳到本快照帧后：既符合 §3.4.1「ackSnapshot ≤ 服务器已广播帧号」，也保持"输入已到达"的语义。
            int ackInput = Math.Min(gate.LastAcceptedFrame(playerId), frame);
            Proto.StateSnapshot snapshot = _differ.BuildFor(frame, authSim, ackInput, viewPos, radius, entityId);
            if (account.BackpressureTier >= 3) TrimFarthest(snapshot, viewPos);   // 档位 3：低优先级实体丢弃（仅增量帧）
            if (snapshot.IsFull) SnapshotFullSent++;

            // ---- 预算守卫（2026-10-18 批）：**发送前**判定，永不让 kcp2k 替我们静默丢 ----
            // 旧实现的教训：SnapshotSent++ 写在 SendTo 之前，包被 kcp2k 丢掉后 Ops 仍报"已发"，
            // 故障在监控上完全隐形。这里先算尺寸，装不下就走降级阶梯并计入 SnapshotDropped。
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

        /// <summary>档位 3：裁掉"距视点最远的"一半实体（低优先级丢弃，§10-E1 第三级）。
        /// **只裁增量帧**——全量帧的"缺席 = 死"镜像语义以全图为前提（见 <see cref="SnapshotDiffer.BuildFor"/>
        /// 全量不裁 AOI 的同一条红线），裁了会让档 3 客户端把视野外实体在镜像里误杀。
        ///
        /// 2026-10-18 起它还有一个调用点：**超预算时的降级第一步**（见 <see cref="SendSnapshot"/>）。
        /// 幂等：已裁过的快照再裁一次不会更小（保留集不变），代价可忽略。</summary>
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
