using System;

namespace LiteSim
{
    /// <summary>
    /// 输入历史环形缓冲（《状态同步实施方案》§5.5）： 保存最近 MaxInputHistory 帧的**全体输入**（每帧独立 SimInputFrame[PlayerCount] 数组 + 逐玩家预测位）。
    /// </summary>
    public sealed class InputHistory
    {
        private readonly SimInputFrame[][] _inputs;
        private readonly bool[][] _predicted;
        private readonly int[] _frameIds;   // 每槽当前持有的帧号（-1 = 未写入）
        private readonly int _capacity;
        private readonly int _playerCount;

        public InputHistory(int capacity, int playerCount)
        {
            _capacity = capacity;
            _playerCount = playerCount;
            _inputs = new SimInputFrame[capacity][];
            _predicted = new bool[capacity][];
            _frameIds = new int[capacity];
            for (int i = 0; i < capacity; i++)
            {
                _inputs[i] = new SimInputFrame[playerCount];
                _predicted[i] = new bool[playerCount];
                _frameIds[i] = -1;
            }
        }

        /// <summary>记录一帧输入（预测或真实；同帧重复 Record = 覆写）。<paramref name="predicted"/> 为 null = 全真实位（服务器侧无预测）。</summary>
        public void Record(int frame, SimInputFrame[] inputs, bool[] predicted)
        {
            int slot = frame % _capacity;
            Array.Copy(inputs, _inputs[slot], _playerCount);
            if (predicted == null)
            {
                for (int i = 0; i < _playerCount; i++) _predicted[slot][i] = false;
            }
            else
            {
                Array.Copy(predicted, _predicted[slot], _playerCount);
            }
            _frameIds[slot] = frame;
        }

        /// <summary>真实输入到达：就地覆盖该帧并清全部预测位；未记录的早到帧亦可直接入史（§5.5）。</summary>
        public void Overwrite(int frame, SimInputFrame[] realInputs)
        {
            int slot = frame % _capacity;
            Array.Copy(realInputs, _inputs[slot], _playerCount);
            for (int i = 0; i < _playerCount; i++) _predicted[slot][i] = false;
            _frameIds[slot] = frame;
        }

        /// <summary>取一帧输入与预测位（返回内部数组——零拷贝；环窗口外 false）。</summary>
        public bool TryGet(int frame, out SimInputFrame[] inputs, out bool[] predicted)
        {
            inputs = null;
            predicted = null;
            if (frame < 0) return false;

            int slot = frame % _capacity;
            if (_frameIds[slot] != frame) return false;

            inputs = _inputs[slot];
            predicted = _predicted[slot];
            return true;
        }

        /// <summary>该帧是否有任一玩家的输入仍为预测（回滚判定用；越界/未记录 false）。</summary>
        public bool IsAnyPredicted(int frame)
        {
            if (frame < 0) return false;

            int slot = frame % _capacity;
            if (_frameIds[slot] != frame) return false;

            for (int i = 0; i < _playerCount; i++)
                if (_predicted[slot][i]) return true;
            return false;
        }

        /// <summary>
        /// 该帧存储输入与给定真实输入是否**逐位不同**（回滚判定用）。
        /// </summary>
        public bool Differs(int frame, SimInputFrame[] real)
        {
            if (frame < 0) return true;

            int slot = frame % _capacity;
            if (_frameIds[slot] != frame) return true;

            for (int i = 0; i < _playerCount; i++)
            {
                ref SimInputFrame a = ref _inputs[slot][i];
                if (a.EntityId != real[i].EntityId) return true; // lint-allow R3（64 位整型 Id 判等，非浮点精度比较）
                if (!BitUtil.Equal(a.MoveX, real[i].MoveX)) return true;
                if (!BitUtil.Equal(a.MoveZ, real[i].MoveZ)) return true;
                if (!BitUtil.Equal(a.AimPointX, real[i].AimPointX)) return true;
                if (!BitUtil.Equal(a.AimPointY, real[i].AimPointY)) return true;
                if (!BitUtil.Equal(a.AimPointZ, real[i].AimPointZ)) return true;
                if (a.Buttons != real[i].Buttons) return true; // lint-allow R3（整型按键位判等，非浮点精度比较）
            }
            return false;
        }
    }
}
