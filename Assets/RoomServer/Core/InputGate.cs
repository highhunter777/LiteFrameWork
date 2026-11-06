using System;
using LiteSim;

using LiteSync.Shared;
namespace RoomServer.Runtime
{
    /// <summary>
    /// 输入闸门（《状态同步实施方案》§4.5-6 两层输入校验的**传输层**；P0 输入面扩展 + R0 正确性收口： 《商业级通用服务端框架总设计》§5 P0-2/P0-3）： 只挡"传输层可见的非法"。
    /// </summary>
    public sealed class InputGate
    {
        /// <summary>未来帧容忍窗（帧号超过 当前帧+此值 = 丢弃——客户端时钟攻击面）。</summary>
        public const int FutureFrameTolerance = 8;

        /// <summary>
        /// Move 单分量绝对值上限（复述 <c>LiteSync.Shared.ProtocolConstants.MoveComponentLimit</c>——
        /// Runtime 层不得引用 LiteNet，一致性由 L1 契约用例钉死）。
        /// </summary>
        public const float MoveComponentLimit = 1f;

        /// <summary>
        /// AimPoint 距离容差（× <c>CombatConfig.HitscanRange</c>）：AimPoint 是**客户端解算的世界点**， 用平方比较不开方。
        /// </summary>
        public const float AimPointRangeTolerance = 1.1f;

        /// <summary>
        /// Move 向量长度平方上限（复述 <c>LiteSync.Shared.ProtocolConstants.VectorLengthSquaredLimit</c>；
        /// 用平方比较避免开方——开方属超越函数纪律管辖区）。
        /// </summary>
        public const float VectorLengthSquaredLimit = 1f;

        /// <summary>最近一次收包中（钳位后的）ackSnapshot——审计/回溯窗口对齐用，判定不依赖。</summary>
        public int LastClampedAckSnapshot { get; private set; } = -1;

        // ---- 计数面（P4 同款收口，与 Ops/BroadcastAccount 同口径）---- 状态私有、只经 <c>Note*</c> 递增：这些是**只增的观测计数**，让它们保持 public 可变字段等于 允许任意调用点直接改写/清零（Ops 行会打印一个被伪造的数）。

        private long _acceptedCount;
        private long _droppedDuplicateFrame;
        private long _droppedOutOfRange;
        private long _droppedStaleFrame;
        private long _droppedAckSnapshot;
        private long _droppedIllegalButtons;
        private long _droppedIllegalActionSeq;
        private long _droppedIllegalWeaponSlot;
        private long _droppedNonFinite;
        private long _droppedIllegalVector;
        private long _droppedOversizedMessage;

        /// <summary>合法输入的接收计数（Ops）。</summary>
        public long AcceptedCount => _acceptedCount;

        /// <summary>同帧重发计数（未消费时走"后到生效"更新路径——只计数、不丢弃；用于冗余/重发观测）。</summary>
        public long DroppedDuplicateFrame => _droppedDuplicateFrame;

        /// <summary>帧号落在容忍窗外 / 空包 / 窗口取不到可用帧（含 <c>frame ≤ 0</c>，P4 起并入此分类）。</summary>
        public long DroppedOutOfRange => _droppedOutOfRange;

        /// <summary>帧号已消费（≤ 服务器当前帧）——不入预存（否则永不消费的滞留项）。</summary>
        public long DroppedStaleFrame => _droppedStaleFrame;

        /// <summary>ackSnapshot 越界（只钳位 + 计数，**不丢输入**；M10 审查定案）。</summary>
        public long DroppedAckSnapshot => _droppedAckSnapshot;

        /// <summary>按键位未定义（含未定义位一律丢弃）。</summary>
        public long DroppedIllegalButtons => _droppedIllegalButtons;

        /// <summary>离散动作 seq 违纪（seq=0 带离散位 / seq 不递增——重放）丢弃计数。</summary>
        public long DroppedIllegalActionSeq => _droppedIllegalActionSeq;

        /// <summary>切枪槽位越界（伪造武器槽）丢弃计数。</summary>
        public long DroppedIllegalWeaponSlot => _droppedIllegalWeaponSlot;

        /// <summary>非有限浮点（NaN/±Infinity）丢弃计数。</summary>
        public long DroppedNonFinite => _droppedNonFinite;

        /// <summary>Move/Aim 分量或长度越界、开火帧零瞄准丢弃计数。</summary>
        public long DroppedIllegalVector => _droppedIllegalVector;

        /// <summary>消息形状非法（repeated frames 超冗余窗上限）丢弃计数。</summary>
        public long DroppedOversizedMessage => _droppedOversizedMessage;

        /// <summary>记一次接受。</summary>
        public void NoteAccepted() => _acceptedCount++;

        /// <summary>记一次同帧重发（只计数、不丢弃）。</summary>
        public void NoteDuplicateFrame() => _droppedDuplicateFrame++;

        /// <summary>记一次帧号/窗口类丢弃。</summary>
        public void NoteOutOfRange() => _droppedOutOfRange++;

        /// <summary>记一次"帧号已消费"。</summary>
        public void NoteStaleFrame() => _droppedStaleFrame++;

        /// <summary>记一次 ackSnapshot 越界（并置钳位后的值——两件事必须同一个原子步骤，故合一个入口）。</summary>
        public void NoteAckSnapshotClamped(int clamped) { _droppedAckSnapshot++; LastClampedAckSnapshot = clamped; }

        /// <summary>记录钳位后的 ackSnapshot（未越界时也照常前推）。</summary>
        public void SetClampedAckSnapshot(int clamped) => LastClampedAckSnapshot = clamped;

        /// <summary>记一次按键位违规丢弃。</summary>
        public void NoteIllegalButtons() => _droppedIllegalButtons++;

        /// <summary>记一次离散动作 seq 违规。</summary>
        public void NoteIllegalActionSeq() => _droppedIllegalActionSeq++;

        /// <summary>记一次武器槽越界。</summary>
        public void NoteIllegalWeaponSlot() => _droppedIllegalWeaponSlot++;

        /// <summary>记一次非有限浮点。</summary>
        public void NoteNonFinite() => _droppedNonFinite++;

        /// <summary>记一次向量越界。</summary>
        public void NoteIllegalVector() => _droppedIllegalVector++;

        /// <summary>记一次消息形状非法（超冗余窗上限）。</summary>
        public void NoteOversizedMessage() => _droppedOversizedMessage++;

        /// <summary>已定义按键位掩码：未定义位一律丢弃（客户端不能凭上报任意位影响判定；服务器内部位 ClientUnreportable 见 <see cref="SimInputFrame.ButtonFireFlag"/>）。</summary>
        public const uint AllowedButtons =
            SimInputFrame.ButtonFire | SimInputFrame.ButtonReload | SimInputFrame.ButtonSwitchWeapon
            | SimInputFrame.ButtonSkill1 | SimInputFrame.ButtonSkill2 | SimInputFrame.ButtonSkill3
            | SimInputFrame.ButtonPickup | SimInputFrame.ButtonUseItem | SimInputFrame.ButtonDodge|SimInputFrame.ButtonAim;

        /// <summary>必须携带非零 Aim 的意图位（射击/施法需要方向；Reload/Pickup/Use 走 target/槽位语义）。</summary>
        private const uint AimRequiredButtons =
            SimInputFrame.ButtonFire | SimInputFrame.ButtonSkill1 | SimInputFrame.ButtonSkill2 | SimInputFrame.ButtonSkill3;

        private readonly int _playerCount;
        /// <summary>房间玩法数值实例（AimPoint 距离闸用——技术债 #1：由房间显式传入，不读全局静态面）。</summary>
        private readonly CombatValues _combat;
        /// <summary>预存输入：定容环，(frame, player) 复合语义（P0 修复多人同帧互顶；R0 换有界存储）。</summary>
        private readonly PendingInputRing _pending;
        /// <summary>各玩家最近被接受的输入帧号（同帧去重）。</summary>
        private readonly int[] _lastAcceptedFrame;
        /// <summary>各玩家最近被接受的离散动作 seq（严格递增判定的基准）。</summary>
        private readonly uint[] _lastActionSeq;
        /// <summary>各玩家最近被接受的离散动作 seq 所在帧（同帧同 seq 的冗余重发 = 良性，见 seq 判定）。</summary>
        private readonly int[] _lastActionSeqFrame;

        public InputGate(int playerCount, CombatValues? combat = null)
        {
            _playerCount = playerCount;
            _combat = combat ?? CombatValues.Default;   // 直构形态（纯协议用例）= 默认表值；生产经 RoomRuntime 传入
            _pending = new PendingInputRing(playerCount);
            _lastAcceptedFrame = new int[playerCount];
            _lastActionSeq = new uint[playerCount];
            _lastActionSeqFrame = new int[playerCount];
            for (int i = 0; i < playerCount; i++) { _lastAcceptedFrame[i] = -1; _lastActionSeqFrame[i] = -1; }
        }

        /// <summary>
        /// 校验并**预存**一条输入（frame 可为未来帧——inputDelay=1 语义）。
        /// </summary>
        public bool Store(in ClientInputBatch batch, int playerId, long entityId, int serverFrame,
            out int acceptedFrame, out SimInputFrame acceptedInput,
            SimVector3? shooterPos = null)
        {
            acceptedFrame = -1;
            acceptedInput = default;

            // ackSnapshot 记账：越界只钳 + 计数，**不丢输入**（M10 审查定案；语义 ACK 验证在会话层）
            if (batch.AckSnapshot < 0 || batch.AckSnapshot > serverFrame)
            {
                NoteAckSnapshotClamped(Math.Clamp(batch.AckSnapshot, 0, serverFrame));
            }
            else
            {
                SetClampedAckSnapshot(batch.AckSnapshot);
            }

            // 消息形状（P0-3）：repeated 超冗余窗上限 = 恶意/损坏——整条丢弃（索引前）
            if (batch.Count > ClientInputBatch.MaxFrames)
            {
                NoteOversizedMessage();
                return false;
            }
            if (batch.Frames == null || batch.Count > batch.Frames.Length)
            {
                NoteOutOfRange();      // 空包/装载不足：无可用帧（与"窗口不含所需帧"同归类）
                return false;
            }

            // 包内最新帧超容忍窗：整条丢弃（时钟攻击面口径不变——预发/冗余的有效帧都在窗内）
            if (batch.Frame > serverFrame + FutureFrameTolerance)
            {
                NoteOutOfRange();
                return false;
            }

            // 冗余窗口取帧：优先"服务器当前帧+1"（inputDelay 正常形态），退而取窗口内已消费帧 （只为识别"整包已消费"，不在本包入库）。
            int frame = -1;
            bool found = false;
            SimInputFrame wire = default;
            for (int f = serverFrame + 1; f >= serverFrame - ClientInputBatch.MaxFrames && !found; f--)
            {
                int offset = batch.Frame - f;
                if (offset >= 0 && offset < batch.Count) { wire = batch.Frames[offset]; frame = f; found = true; }
            }

            if (!found || frame <= 0 || frame > serverFrame + FutureFrameTolerance)
            {
                NoteOutOfRange();
                return false;
            }

            // 已消费帧：权威只前进（帧号 = 已执行步数），≤ serverFrame 的帧永不再被 TryConsume → 不入预存。
            // 此时窗口整体 ≤ serverFrame（主帧是窗内最大的可取帧）——未来帧预存也必为空，直接整包拒绝。
            if (frame <= serverFrame)
            {
                NoteStaleFrame();
                return false;
            }

            // 主帧校验（任一违规整条丢弃——安全口径不变；**重复帧不再整条丢弃**：未消费则后到生效）
            if (!ValidateWire(wire, frame, playerId, shooterPos)) return false;

            // 接受记账：**同帧未消费 = 更新**（客户端"最新采样"与"后到生效"对齐）——只计数、不丢弃
            if (frame <= _lastAcceptedFrame[playerId]) NoteDuplicateFrame();   // 冗余重发计数（值照常覆盖）
            else _lastAcceptedFrame[playerId] = frame;
            if ((wire.Buttons & SimInputFrame.DiscreteIntentButtons) != 0u && wire.ActionSeq > _lastActionSeq[playerId])
            {
                _lastActionSeq[playerId] = wire.ActionSeq;
                _lastActionSeqFrame[playerId] = frame;
            }

            var input = wire;
            input.EntityId = entityId;                                  // EntityId 防伪覆写
            _pending.Store(frame, playerId, input);
            NoteAccepted();
            acceptedFrame = frame;
            acceptedInput = input;

            // 未来帧预存：窗口内 (serverFrame+1, serverFrame+容忍窗] 的帧逐帧校验后一并入库。)。
            int futureMax = serverFrame + FutureFrameTolerance;
            for (int f = serverFrame + 2; f <= futureMax && f <= batch.Frame; f++)
            {
                int offset = batch.Frame - f;
                if (offset < 0 || offset >= batch.Count) continue;      // 不在窗口
                SimInputFrame extra = batch.Frames[offset];
                if (!ValidateWire(extra, f, playerId, shooterPos)) continue;

                if (f <= _lastAcceptedFrame[playerId]) NoteDuplicateFrame();
                else _lastAcceptedFrame[playerId] = f;
                if ((extra.Buttons & SimInputFrame.DiscreteIntentButtons) != 0u && extra.ActionSeq > _lastActionSeq[playerId])
                {
                    _lastActionSeq[playerId] = extra.ActionSeq;
                    _lastActionSeqFrame[playerId] = f;
                }

                extra.EntityId = entityId;                              // EntityId 防伪覆写（同主帧口径）
                _pending.Store(f, playerId, extra);
                NoteAccepted();
            }
            return true;
        }

        /// <summary>
        /// 单帧逐项校验（主帧与预存未来帧同口径；违规即按原因计数并返回 false）： 按键白名单 → 武器槽范围 → 离散 seq 纪律 → 数值边界（有限/向量/瞄准点）。
        /// </summary>
        private bool ValidateWire(in SimInputFrame wire, int frame, int playerId, SimVector3? shooterPos)
        {
            // 按键位白名单：未定义位（含服务器内部位的伪造上报）一律丢弃——否则伪造 ButtonFireFlag 可绕过回溯补判语义
            if ((wire.Buttons & ~AllowedButtons) != 0u)
            {
                NoteIllegalButtons();
                return false;
            }

            // 切枪槽位范围（§5.4"伪造武器槽"）：越界 = 伪造（合法 SDK 不构造），整条丢弃。
            if ((wire.Buttons & SimInputFrame.ButtonSwitchWeapon) != 0u
                && (wire.SelectedWeaponSlot < 0 || wire.SelectedWeaponSlot >= SimConfig.WeaponSlotsPerEntity))
            {
                NoteIllegalWeaponSlot();
                return false;
            }

            // 离散意图 seq 纪律（§3.2/§5.4）：离散位必带非零 seq；seq 不严格递增 = 重放/迟到 → 丢弃。
            // **同帧同 seq 例外**：那是同一请求的冗余重发（未消费时按"后到生效"更新，值本身逐位相同）。
            uint seq = wire.ActionSeq;
            if ((wire.Buttons & SimInputFrame.DiscreteIntentButtons) != 0u)
            {
                bool sameRequestRepeat = seq == _lastActionSeq[playerId] && frame == _lastActionSeqFrame[playerId];
                if (seq == 0u || seq < _lastActionSeq[playerId] || (seq == _lastActionSeq[playerId] && !sameRequestRepeat))
                {
                    NoteIllegalActionSeq();
                    return false;
                }
            }

            // 数值边界（R0-P0-3：非法浮点不得进入权威状态——NaN/Infinity 会毒化移动/射击判定）
            if (!float.IsFinite(wire.MoveX) || !float.IsFinite(wire.MoveZ)
                || !float.IsFinite(wire.AimPointX) || !float.IsFinite(wire.AimPointY) || !float.IsFinite(wire.AimPointZ))
            {
                NoteNonFinite();
                return false;
            }

            // Move：分量与长度 ≤ 1（采集侧契约的服务器侧强制）；比较用乘加不开方（无超越函数纪律问题）
            if (wire.MoveX > MoveComponentLimit || wire.MoveX < -MoveComponentLimit
                || wire.MoveZ > MoveComponentLimit || wire.MoveZ < -MoveComponentLimit)
            {
                NoteIllegalVector();
                return false;
            }
            if (wire.MoveX * wire.MoveX + wire.MoveZ * wire.MoveZ > VectorLengthSquaredLimit)
            {
                NoteIllegalVector();
                return false;
            }

            // AimPoint：**全零 = 无点**（合法输入形态——相机未就绪/退化帧，不产命中）； Fire/Skill 意图帧必须带非零点（无点开火/施法 = 非法——瞄准方向字段已删， 点是非零契约的唯一载体；《固定斜视角射击方案专项设计》§4）。
            bool aimPointZero = wire.AimPointX == 0f && wire.AimPointY == 0f && wire.AimPointZ == 0f;
            bool aimNeeded = (wire.Buttons & AimRequiredButtons) != 0u;
            if (aimNeeded && aimPointZero)   // lint-allow R3（点值为零 = 契约边界判定，非浮点精度比较）
            {
                NoteIllegalVector();
                return false;
            }

            // **AimPoint 距离闸**（反作弊面）：客户端上报世界坐标点， 权威端必须约束它"打得到"——否则可送超远点让服务器从枪口朝任意方向扫射。
            if (shooterPos != null && !aimPointZero)
            {
                float dxp = wire.AimPointX - shooterPos.Value.X;
                float dyp = wire.AimPointY - shooterPos.Value.Y;
                float dzp = wire.AimPointZ - shooterPos.Value.Z;
                float distSq = dxp * dxp + dyp * dyp + dzp * dzp;
                float maxAimPoint = _combat.HitscanRange * AimPointRangeTolerance;
                if (distSq > maxAimPoint * maxAimPoint)
                {
                    NoteIllegalVector();
                    return false;
                }
            }
            return true;
        }

        /// <summary>消费：权威帧推进到 f 时取该玩家预存输入；无预存 = 空输入沿用（掉线/迟到）。</summary>
        public bool TryConsume(int frame, int playerId, out SimInputFrame input)
        {
            if (playerId < 0 || playerId >= _playerCount) { input = default; return false; }
            return _pending.TryConsume(frame, playerId, out input);
        }

        /// <summary>
        /// 该玩家最近被接受的输入帧号（作为该客户端已确认的输入下限，随快照 ack_input 下发；-1 = 尚无）。
        /// </summary>
        public int LastAcceptedFrame(int playerId) =>
            playerId >= 0 && playerId < _playerCount ? _lastAcceptedFrame[playerId] : -1;
    }
}
