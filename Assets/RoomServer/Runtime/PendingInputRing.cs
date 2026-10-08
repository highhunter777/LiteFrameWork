using LiteSim;

namespace RoomServer.Runtime
{
    /// <summary>
    /// 预存输入定容环（《商业级通用服务端框架总设计》§5 P0-2：入闸输入的**唯一存储**——
    /// 字典换定容环：运行期零分配、容量有界（防慢客户端/时钟攻击把预存表撑成无界内存））。
    ///
    /// 布局：slot = frame % <see cref="Capacity"/>；每 slot = 帧号 + 每玩家输入数组 + 有效位图。
    /// **slot 复用必须完整清理**（帧号换代即清空数组与位图）——否则上一代帧的陈旧输入会
    /// 在环回绕后"复活"（幽灵输入 = 判定分叉，比丢帧严重得多）。
    ///
    /// 容量推导：需覆盖 [当前帧 − 冗余窗, 当前帧 + 未来容忍窗] 的全部帧号——
    /// <c>FutureFrameTolerance(8) + ClientInputBatch.MaxFrames(16) + 1 = 25 ≤ 32</c>（32 = 2 的幂，取模用掩码）。
    /// 越界帧号在 <see cref="InputGate"/> 先验证后索引（P0-2：验证永远先于索引），环内不会见到窗口外的帧。
    /// </summary>
    public sealed class PendingInputRing
    {
        /// <summary>环容量（2 的幂；推导见类注释——改容忍窗/冗余窗时必须同步重推）。</summary>
        public const int Capacity = 32;

        private const int CapacityMask = Capacity - 1;

        private readonly int _playerCapacity;
        private readonly int _validWordCount;
        private readonly int[] _slotFrame = new int[Capacity];                       // 槽位持有的帧号（0 = 未用——帧号从 1 起合法）
        private readonly SimInputFrame[][] _slotInputs = new SimInputFrame[Capacity][];
        private readonly uint[][] _slotValid = new uint[Capacity][];                  // 每玩家有效位图（player ≤ 32）

        public PendingInputRing(int playerCapacity)
        {
            if (playerCapacity <= 0 || playerCapacity > 32)
                throw new System.ArgumentOutOfRangeException(nameof(playerCapacity), playerCapacity,
                    "玩家容量必须在 1..32（valid 位图为单 uint）");
            _playerCapacity = playerCapacity;
            _validWordCount = 1;
            for (int i = 0; i < Capacity; i++)
            {
                _slotInputs[i] = new SimInputFrame[playerCapacity];
                _slotValid[i] = new uint[_validWordCount];
                _slotFrame[i] = 0;
            }
        }

        /// <summary>预存一条输入（调用方保证 frame 已过闸）。同帧同玩家后到覆盖（正常冗余下不会发生——同帧去重在闸门挡）。</summary>
        public void Store(int frame, int playerId, in SimInputFrame input)
        {
            int slot = frame & CapacityMask;
            if (_slotFrame[slot] != frame)
            {
                // slot 换代：完整清理（P0-2 红线——陈旧输入不得复活）
                _slotFrame[slot] = frame;
                System.Array.Clear(_slotInputs[slot], 0, _playerCapacity);
                System.Array.Clear(_slotValid[slot], 0, _validWordCount);
            }

            _slotInputs[slot][playerId] = input;
            _slotValid[slot][0] |= 1u << playerId;
        }

        /// <summary>消费 (frame, playerId)：命中则取走并清位（每帧每玩家只消费一次）；未预存 = 空输入沿用。</summary>
        public bool TryConsume(int frame, int playerId, out SimInputFrame input)
        {
            input = default;
            if (playerId < 0 || playerId >= _playerCapacity) return false;

            int slot = frame & CapacityMask;
            if (_slotFrame[slot] != frame) return false;

            uint bit = 1u << playerId;
            if ((_slotValid[slot][0] & bit) == 0u) return false;

            input = _slotInputs[slot][playerId];
            _slotValid[slot][0] &= ~bit;                       // 消费即出队
            return true;
        }
    }
}
