using System;
using System.Collections.Generic;
using LiteNet;
using LiteNet.Protocol;
using LiteNet.Proto;
using LiteSim;

namespace RoomServer
{
    /// <summary>
    /// 输入闸门（《状态同步实施方案》§4.5-6 两层输入校验的**传输层**；P0 输入面扩展：《游戏业务系统总设计》§3.2/§3.3）：
    /// 只挡"传输层可见的非法"，不做语义校验（语义 clamp 在 Sim 内——§4.5-6 第二层，两端一致由 M8 Sim 保证）。
    ///
    /// 职责（2026-09-17 重构：校验/存储与消费分离——客户端按 inputDelay=1 发**未来帧**输入，
    /// 本类按帧号**预存**，权威帧推进到位时由 Room 消费）：
    /// - **帧号合法性**：frame ≤ 0 或 frame > 服务器当前帧 + 容忍窗（未来帧时钟攻击面）丢弃。
    /// - **同帧去重**：每帧每玩家至多 1 条（首条生效——冗余包重复不重复消费）。
    /// - **EntityId 防伪**：客户端上报的 EntityId 一律**覆写**为该会话所属实体 Id。
    /// - **ackSnapshot**：合法性记账（负值/超前**钳位 + 计数，不丢输入**；判定不消费它——回溯窗口由
    ///   `LagCompensator` 自行 clamp。2026-09-19 修正：原先"超前即丢整条"会丢掉合法输入，因为服务器
    ///   下发的 ack 恰可能等于"下一待处理帧"）。
    /// - **已消费帧**（frame ≤ 服务器当前帧）：拒绝（权威只前进，存了就是永不消费的滞留项）。
    /// - **按键位白名单**（P0 扩）：未定义位一律丢弃（客户端不能凭上报任意位影响判定；服务器内部位
    ///   ClientUnreportable 见 <see cref="SimInputFrame.ButtonFireFlag"/>）。
    /// - **action_seq 纪律**（P0 扩，§5.4"服务器拒绝…重复 action_seq"）：离散意图位
    ///   （Reload/SwitchWeapon/Skill1..3/Pickup/UseItem）必须携带**非零且逐玩家严格递增**的 seq——
    ///   重放/迟到重复请求丢弃（冗余窗口的同帧重复已由同帧去重挡住，这里的 seq 判定挡的是
    ///   **跨帧**重放）。Fire/移动是连续意图，不带 seq、不查。
    /// - **切枪槽位范围**（P0 扩，§5.4"伪造武器槽"拒绝）：SwitchWeapon 意图的 selected_weapon_slot
    ///   越界丢弃（合法客户端 SDK 不会构造；范围是传输层可见事实）。
    /// </summary>
    public sealed class InputGate
    {
        /// <summary>未来帧容忍窗（帧号超过 当前帧+此值 = 丢弃——客户端时钟攻击面）。</summary>
        public const int FutureFrameTolerance = 8;

        /// <summary>最近一次收包中（钳位后的）ackSnapshot——审计/上报用，判定不依赖。</summary>
        public int LastClampedAckSnapshot = -1;

        /// <summary>合法输入的接收计数（Ops）。</summary>
        public long AcceptedCount;

        /// <summary>非法/冗余输入的丢弃计数（Ops：按原因分列）。</summary>
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

        /// <summary>已定义按键位掩码：未定义位一律丢弃（客户端不能凭上报任意位影响判定；服务器内部位 ClientUnreportable 见 <see cref="SimInputFrame.ButtonFireFlag"/>）。</summary>
        public const uint AllowedButtons =
            SimInputFrame.ButtonFire | SimInputFrame.ButtonReload | SimInputFrame.ButtonSwitchWeapon
            | SimInputFrame.ButtonSkill1 | SimInputFrame.ButtonSkill2 | SimInputFrame.ButtonSkill3
            | SimInputFrame.ButtonPickup | SimInputFrame.ButtonUseItem | SimInputFrame.ButtonDodge;

        private readonly int _playerCount;
        /// <summary>预存输入：按 (playerId, frame) 键——**帧号单键会让多玩家同帧输入互相覆盖**
        /// （先到者被后到者顶掉、TryConsume 只有一人拿到输入，2026-09-22 P0 审查定位的真缺陷：
        /// 2 人房里每人每帧约 50% 概率丢一帧移动输入）。值结构 = (playerId*大跨度 + frame) 复合键。</summary>
        private readonly Dictionary<long, SimInputFrame> _pending = new Dictionary<long, SimInputFrame>();
        /// <summary>各玩家最近被接受的输入帧号（同帧去重）。</summary>
        private readonly int[] _lastAcceptedFrame;
        /// <summary>各玩家最近被接受的离散动作 seq（严格递增判定的基准）。</summary>
        private readonly uint[] _lastActionSeq;

        private static long PendingKey(int playerId, int frame) => (long)playerId * int.MaxValue + frame;

        public InputGate(int playerCount)
        {
            _playerCount = playerCount;
            _lastAcceptedFrame = new int[playerCount];
            _lastActionSeq = new uint[playerCount];
            for (int i = 0; i < playerCount; i++) _lastAcceptedFrame[i] = -1;
        }

        /// <summary>
        /// 校验并**预存**一条输入（frame 可为未来帧——inputDelay=1 语义）。
        /// EntityId 覆写为会话所属实体（防伪）。返回 false = 校验失败已丢弃（调用方无需处理）。
        ///
        /// 2026-09-19 审查修正三处：
        /// ① **ackSnapshot 不再导致丢输入**：ack 是**元数据**（判定不消费它；回溯窗口由
        ///    <see cref="LagCompensator"/> 自行 clamp），且服务器自己下发的 ack 可能等于"下一待处理帧"
        ///    （见 <see cref="LastAcceptedFrame"/>），原先"ack &gt; serverFrame 即整条丢弃"会**丢掉合法输入**
        ///    （服务器只能用空输入推进该帧）。现在：越界 ack **只计数 + 钳到 serverFrame**，输入照常处理。
        /// ② **拒绝已消费帧**（frame ≤ serverFrame）：权威只向前推进，这类帧永不会被消费 → 存了就是滞留。
        /// ③ **输出实际接受的帧与输入**（`out acceptedFrame/acceptedInput`）：调用方（开火/回溯判定）
        ///    必须用"真正被接受的帧"，而不是自己再推一遍取帧口径——否则两处口径一旦分叉，
        ///    会出现"输入收了（真开枪）但没记回溯判定"的不一致。
        /// </summary>
        public bool Store(InputMessage msg, int playerId, long entityId, int serverFrame,
            out int acceptedFrame, out SimInputFrame acceptedInput)
        {
            acceptedFrame = -1;
            acceptedInput = default;

            // ackSnapshot 记账：越界只钳 + 计数，**不丢输入**（见上 ①）
            if (msg.AckSnapshot < 0 || msg.AckSnapshot > serverFrame)
            {
                DroppedAckSnapshot++;
                LastClampedAckSnapshot = Math.Clamp(msg.AckSnapshot, 0, serverFrame);
            }
            else
            {
                LastClampedAckSnapshot = msg.AckSnapshot;
            }

            // 冗余窗口取帧：优先"服务器当前帧+1"（inputDelay 正常形态），退而取窗口内**未消费的**最大帧
            int frame = -1;
            InputFrame wire = null;
            for (int f = serverFrame + 1; f >= serverFrame - InputPacker.MaxRedundancy && wire == null; f--)
            {
                int offset = msg.Frame - f;
                if (offset >= 0 && offset < msg.Frames.Count) { wire = msg.Frames[offset]; frame = f; }
            }

            if (wire == null || frame <= 0 || frame > serverFrame + FutureFrameTolerance)
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

            // P0 切枪槽位范围（§5.4"伪造武器槽"）：越界 = 伪造（合法 SDK 不构造），整条丢弃。
            if ((wire.Buttons & SimInputFrame.ButtonSwitchWeapon) != 0u
                && (wire.SelectedWeaponSlot < 0 || wire.SelectedWeaponSlot >= SimConfig.WeaponSlotsPerEntity))
            {
                DroppedIllegalWeaponSlot++;
                return false;
            }

            // 同帧去重：每帧每玩家至多 1 条（首条生效）——必须先于 seq 判定：
            // 冗余重发（同帧同 seq）是良性重复，归这里；seq 判定只挡**跨帧**重放（§4.2 冗余窗口的补帧恢复依赖此顺序）
            if (frame <= _lastAcceptedFrame[playerId])
            {
                DroppedDuplicateFrame++;
                return false;
            }

            // P0 离散意图 seq 纪律（§3.2/§5.4）：离散位必带非零 seq；seq 不严格递增 = 重放/迟到 → 丢弃
            uint seq = wire.ActionSeq;
            if ((wire.Buttons & SimInputFrame.DiscreteIntentButtons) != 0u
                && (seq == 0u || seq <= _lastActionSeq[playerId]))
            {
                DroppedIllegalActionSeq++;
                return false;
            }

            _lastAcceptedFrame[playerId] = frame;
            if ((wire.Buttons & SimInputFrame.DiscreteIntentButtons) != 0u) _lastActionSeq[playerId] = seq;

            var input = new SimInputFrame
            {
                EntityId = entityId,                                  // EntityId 防伪覆写
                MoveX = wire.MoveX, MoveZ = wire.MoveZ,
                AimX = wire.AimX, AimZ = wire.AimZ,
                Buttons = wire.Buttons,
                SelectedWeaponSlot = wire.SelectedWeaponSlot,
                TargetEntityId = wire.TargetEntityId,
                ActionSeq = seq,
            };
            _pending[PendingKey(playerId, frame)] = input;
            AcceptedCount++;
            acceptedFrame = frame;
            acceptedInput = input;
            return true;
        }

        /// <summary>消费：权威帧推进到 f 时取该玩家预存输入；无预存 = 空输入沿用（掉线/迟到）。</summary>
        public bool TryConsume(int frame, int playerId, out SimInputFrame input)
        {
            if (playerId < 0 || playerId >= _playerCount) { input = default; return false; }
            if (_pending.TryGetValue(PendingKey(playerId, frame), out input))
            {
                _pending.Remove(PendingKey(playerId, frame));
                return true;
            }
            input = default;
            return false;
        }

        /// <summary>
        /// 该玩家最近被接受的输入帧号（作为该客户端已确认的输入下限，随快照 ack_input 下发；-1 = 尚无）。
        /// 口径说明：这是"最新输入帧"而非"已消费帧"——服务器广播 ack_input 的用途是让客户端知道
        /// **它的输入已到达服务器**，回滚判定以真实输入帧号为准，不依赖此值。
        /// </summary>
        public int LastAcceptedFrame(int playerId) =>
            playerId >= 0 && playerId < _playerCount ? _lastAcceptedFrame[playerId] : -1;
    }
}
