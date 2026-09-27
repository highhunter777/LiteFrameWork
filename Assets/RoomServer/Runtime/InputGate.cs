using System;
using LiteSim;

namespace RoomServer.Runtime
{
    /// <summary>
    /// 输入闸门（《状态同步实施方案》§4.5-6 两层输入校验的**传输层**；P0 输入面扩展 + R0 正确性收口：
    /// 《商业级通用服务端框架总设计》§5 P0-2/P0-3）：
    /// 只挡"传输层可见的非法"，不做语义校验（语义 clamp 在 Sim 内——§4.5-6 第二层，两端一致由 M8 Sim 保证）。
    ///
    /// R1 纯化：入参从 proto <c>InputMessage</c> 换为纯数据 <see cref="ClientInputBatch"/>（§8.1
    /// "RoomRuntime 不见 proto"）——校验语义逐条不变，两端常量一致性由 L1 契约用例钉死。
    ///
    /// 职责（2026-09-17 重构：校验/存储与消费分离——客户端按 inputDelay=1 发**未来帧**输入，
    /// 本类按帧号**预存**，权威帧推进到位时由 <see cref="RoomRuntime"/> 消费）：
    /// - **消息形状**（R0-P0-3）：repeated frames 数量超冗余窗上限 = 恶意/损坏包，整条丢弃。
    /// - **帧号合法性**：frame ≤ 0 或 frame > 服务器当前帧 + 容忍窗（未来帧时钟攻击面）丢弃。
    /// - **同帧去重**：每帧每玩家至多 1 条（首条生效——冗余包重复不重复消费）。
    /// - **EntityId 防伪**：客户端上报的 EntityId 一律**覆写**为该会话所属实体 Id。
    /// - **ackSnapshot**：合法性记账（负值/超前**钳位 + 计数，不丢输入**；语义 ACK 的可信化在
    ///   App 层会话（发送 ledger 验证）；本处钳位值只供回溯窗口对齐参考）。
    /// - **已消费帧**（frame ≤ 服务器当前帧）：拒绝（权威只前进，存了就是永不消费的滞留项）。
    /// - **按键位白名单**：未定义位一律丢弃（客户端不能凭上报任意位影响判定；服务器内部位
    ///   ClientUnreportable 见 <see cref="SimInputFrame.ButtonFireFlag"/>）。
    /// - **action_seq 纪律**：离散意图位（Reload/SwitchWeapon/Skill1..3/Pickup/UseItem）必须携带
    ///   **非零且逐玩家严格递增**的 seq——重放/迟到重复请求丢弃（同帧重复归同帧去重，seq 挡跨帧重放）。
    /// - **切枪槽位范围**：SwitchWeapon 意图的 selected_weapon_slot 越界丢弃（伪造武器槽拒绝）。
    /// - **数值边界**（R0-P0-3：非法包不得进入权威状态）：Move/Aim 四浮点必须有限；
    ///   Move 分量与长度 ≤ 1；Aim 分量与长度 ≤ 1；Fire/Skill 意图帧 Aim 必须非零（射击/施法没有方向 = 非法）。
    ///
    /// 存储（R0-P0-2）：过闸输入进 <see cref="PendingInputRing"/> 定容环（(frame, player) 复合键语义不变——
    /// 2026-09-22 Sync-P0 已修多玩家同帧互顶缺陷；R0 把字典换定容环：零分配、容量有界、slot 复用全清）。
    /// </summary>
    public sealed class InputGate
    {
        /// <summary>未来帧容忍窗（帧号超过 当前帧+此值 = 丢弃——客户端时钟攻击面）。</summary>
        public const int FutureFrameTolerance = 8;

        /// <summary>
        /// Move/Aim 单分量绝对值上限（复述 <c>LiteNet.Protocol.ProtocolConstants.MoveComponentLimit</c>——
        /// Runtime 层不得引用 LiteNet，一致性由 L1 契约用例钉死）。
        /// </summary>
        public const float MoveComponentLimit = 1f;

        /// <summary>
        /// Move/Aim 向量长度平方上限（复述 <c>LiteNet.Protocol.ProtocolConstants.VectorLengthSquaredLimit</c>；
        /// 用平方比较避免开方——开方属超越函数纪律管辖区）。
        /// </summary>
        public const float VectorLengthSquaredLimit = 1f;

        /// <summary>最近一次收包中（钳位后的）ackSnapshot——审计/回溯窗口对齐用，判定不依赖。</summary>
        public int LastClampedAckSnapshot = -1;

        /// <summary>合法输入的接收计数（Ops）。</summary>
        public long AcceptedCount;

        /// <summary>非法/冗余输入的丢弃计数（Ops：按原因分列——P0-3"统一拒绝并计数"）。</summary>
        public long DroppedIllegalFrame;
        public long DroppedDuplicateFrame;
        public long DroppedOutOfRange;
        /// <summary>帧号已消费（≤ 服务器当前帧）——不入预存（否则永不消费的滞留项）。</summary>
        public long DroppedStaleFrame;
        public long DroppedAckSnapshot;
        public long DroppedIllegalButtons;
        /// <summary>离散动作 seq 违纪（seq=0 带离散位 / seq 不递增——重放）丢弃计数。</summary>
        public long DroppedIllegalActionSeq;
        /// <summary>切枪槽位越界（伪造武器槽）丢弃计数。</summary>
        public long DroppedIllegalWeaponSlot;
        /// <summary>非有限浮点（NaN/±Infinity）丢弃计数。</summary>
        public long DroppedNonFinite;
        /// <summary>Move/Aim 分量或长度越界、开火帧零瞄准丢弃计数。</summary>
        public long DroppedIllegalVector;
        /// <summary>消息形状非法（repeated frames 超冗余窗上限）丢弃计数。</summary>
        public long DroppedOversizedMessage;

        /// <summary>已定义按键位掩码：未定义位一律丢弃（客户端不能凭上报任意位影响判定；服务器内部位 ClientUnreportable 见 <see cref="SimInputFrame.ButtonFireFlag"/>）。</summary>
        public const uint AllowedButtons =
            SimInputFrame.ButtonFire | SimInputFrame.ButtonReload | SimInputFrame.ButtonSwitchWeapon
            | SimInputFrame.ButtonSkill1 | SimInputFrame.ButtonSkill2 | SimInputFrame.ButtonSkill3
            | SimInputFrame.ButtonPickup | SimInputFrame.ButtonUseItem | SimInputFrame.ButtonDodge|SimInputFrame.ButtonAim;

        /// <summary>必须携带非零 Aim 的意图位（射击/施法需要方向；Reload/Pickup/Use 走 target/槽位语义）。</summary>
        private const uint AimRequiredButtons =
            SimInputFrame.ButtonFire | SimInputFrame.ButtonSkill1 | SimInputFrame.ButtonSkill2 | SimInputFrame.ButtonSkill3;

        private readonly int _playerCount;
        /// <summary>预存输入：定容环，(frame, player) 复合语义（P0 修复多人同帧互顶；R0 换有界存储）。</summary>
        private readonly PendingInputRing _pending;
        /// <summary>各玩家最近被接受的输入帧号（同帧去重）。</summary>
        private readonly int[] _lastAcceptedFrame;
        /// <summary>各玩家最近被接受的离散动作 seq（严格递增判定的基准）。</summary>
        private readonly uint[] _lastActionSeq;

        public InputGate(int playerCount)
        {
            _playerCount = playerCount;
            _pending = new PendingInputRing(playerCount);
            _lastAcceptedFrame = new int[playerCount];
            _lastActionSeq = new uint[playerCount];
            for (int i = 0; i < playerCount; i++) _lastAcceptedFrame[i] = -1;
        }

        /// <summary>
        /// 校验并**预存**一条输入（frame 可为未来帧——inputDelay=1 语义）。
        /// EntityId 覆写为会话所属实体（防伪）。返回 false = 校验失败已丢弃（调用方无需处理）。
        ///
        /// 判定顺序纪律（P0-2"先验证后索引" + 计数语义）：
        /// 形状 → 帧号/按键/槽位（结构）→ 同帧去重 → seq → 数值边界 → 接受记账。
        /// 接受记账（_lastAcceptedFrame/_lastActionSeq）只在**全部通过**时更新——
        /// 非法包不烧帧槽/seq 槽（同帧的合法重发仍可被接受）。
        /// </summary>
        public bool Store(in ClientInputBatch batch, int playerId, long entityId, int serverFrame,
            out int acceptedFrame, out SimInputFrame acceptedInput)
        {
            acceptedFrame = -1;
            acceptedInput = default;

            // ackSnapshot 记账：越界只钳 + 计数，**不丢输入**（M10 审查定案；语义 ACK 验证在会话层）
            if (batch.AckSnapshot < 0 || batch.AckSnapshot > serverFrame)
            {
                DroppedAckSnapshot++;
                LastClampedAckSnapshot = Math.Clamp(batch.AckSnapshot, 0, serverFrame);
            }
            else
            {
                LastClampedAckSnapshot = batch.AckSnapshot;
            }

            // 消息形状（P0-3）：repeated 超冗余窗上限 = 恶意/损坏——整条丢弃（索引前）
            if (batch.Count > ClientInputBatch.MaxFrames)
            {
                DroppedOversizedMessage++;
                return false;
            }
            if (batch.Frames == null || batch.Count > batch.Frames.Length)
            {
                DroppedOutOfRange++;      // 空包/装载不足：无可用帧（与"窗口不含所需帧"同归类）
                return false;
            }

            // 冗余窗口取帧：优先"服务器当前帧+1"（inputDelay 正常形态），退而取窗口内**未消费的**最大帧
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
                DroppedOutOfRange++;
                return false;
            }

            // 已消费帧：权威只前进（帧号 = 已执行步数），≤ serverFrame 的帧永不再被 TryConsume → 不入预存
            if (frame <= serverFrame)
            {
                DroppedStaleFrame++;
                return false;
            }

            // 按键位白名单：未定义位（含服务器内部位的伪造上报）一律丢弃——否则伪造 ButtonFireFlag 可绕过回溯补判语义
            if ((wire.Buttons & ~AllowedButtons) != 0u)
            {
                DroppedIllegalButtons++;
                return false;
            }

            // 切枪槽位范围（§5.4"伪造武器槽"）：越界 = 伪造（合法 SDK 不构造），整条丢弃。
            if ((wire.Buttons & SimInputFrame.ButtonSwitchWeapon) != 0u
                && (wire.SelectedWeaponSlot < 0 || wire.SelectedWeaponSlot >= SimConfig.WeaponSlotsPerEntity))
            {
                DroppedIllegalWeaponSlot++;
                return false;
            }

            // 同帧去重：每帧每玩家至多 1 条（首条生效）——先于 seq 判定：
            // 冗余重发（同帧同 seq）是良性重复，归这里；seq 判定只挡**跨帧**重放（§4.2 冗余窗口补帧恢复依赖此顺序）
            if (frame <= _lastAcceptedFrame[playerId])
            {
                DroppedDuplicateFrame++;
                return false;
            }

            // 离散意图 seq 纪律（§3.2/§5.4）：离散位必带非零 seq；seq 不严格递增 = 重放/迟到 → 丢弃
            uint seq = wire.ActionSeq;
            if ((wire.Buttons & SimInputFrame.DiscreteIntentButtons) != 0u
                && (seq == 0u || seq <= _lastActionSeq[playerId]))
            {
                DroppedIllegalActionSeq++;
                return false;
            }

            // 数值边界（R0-P0-3：非法浮点不得进入权威状态——NaN/Infinity 会毒化移动/射击判定）
            if (!float.IsFinite(wire.MoveX) || !float.IsFinite(wire.MoveZ)
                || !float.IsFinite(wire.AimX) || !float.IsFinite(wire.AimZ))
            {
                DroppedNonFinite++;
                return false;
            }

            // Move：分量与长度 ≤ 1（采集侧契约的服务器侧强制）；比较用乘加不开方（无超越函数纪律问题）
            if (wire.MoveX > MoveComponentLimit || wire.MoveX < -MoveComponentLimit
                || wire.MoveZ > MoveComponentLimit || wire.MoveZ < -MoveComponentLimit)
            {
                DroppedIllegalVector++;
                return false;
            }
            if (wire.MoveX * wire.MoveX + wire.MoveZ * wire.MoveZ > VectorLengthSquaredLimit)
            {
                DroppedIllegalVector++;
                return false;
            }

            // Aim：分量与长度 ≤ 1；Fire/Skill 意图帧必须非零（零方向开火/施法 = 非法，Sim 侧 atan2 也会失义）
            if (wire.AimX > MoveComponentLimit || wire.AimX < -MoveComponentLimit
                || wire.AimZ > MoveComponentLimit || wire.AimZ < -MoveComponentLimit)
            {
                DroppedIllegalVector++;
                return false;
            }
            bool aimNeeded = (wire.Buttons & AimRequiredButtons) != 0u;
            float aimLengthSquared = wire.AimX * wire.AimX + wire.AimZ * wire.AimZ;
            if (aimLengthSquared > VectorLengthSquaredLimit
                || (aimNeeded && aimLengthSquared <= 0f))   // lint-allow R3（长度平方与 0 常量比较，采集边界判定）
            {
                DroppedIllegalVector++;
                return false;
            }

            // 全部通过：接受记账 + 预存
            _lastAcceptedFrame[playerId] = frame;
            if ((wire.Buttons & SimInputFrame.DiscreteIntentButtons) != 0u) _lastActionSeq[playerId] = seq;

            var input = wire;
            input.EntityId = entityId;                                  // EntityId 防伪覆写
            _pending.Store(frame, playerId, input);
            AcceptedCount++;
            acceptedFrame = frame;
            acceptedInput = input;
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
        /// 口径说明：这是"最新输入帧"而非"已消费帧"——服务器广播 ack_input 的用途是让客户端知道
        /// **它的输入已到达服务器**，回滚判定以真实输入帧为准，不依赖此值。
        /// </summary>
        public int LastAcceptedFrame(int playerId) =>
            playerId >= 0 && playerId < _playerCount ? _lastAcceptedFrame[playerId] : -1;
    }
}
