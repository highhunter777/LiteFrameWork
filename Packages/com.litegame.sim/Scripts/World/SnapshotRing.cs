namespace LiteSim
{
    /// <summary>
    /// 快照环形缓冲（《状态同步实施方案》§5.2）： 每槽**独立 SimWorldState 实例**（构造期预分配，运行期零 new）。
    /// </summary>
    public sealed class SnapshotRing
    {
        private readonly SimWorldState[] _slots;
        private readonly int[] _frames;      // 每槽当前持有的帧号（-1 = 未写入）
        private readonly int _capacity;

        public SnapshotRing(int capacity)
        {
            _capacity = capacity;
            _slots = new SimWorldState[capacity];
            _frames = new int[capacity];
            for (int i = 0; i < capacity; i++)
            {
                _slots[i] = new SimWorldState();
                _frames[i] = -1;
            }
        }

        /// <summary>捕获快照（每帧 Step 后调用；frame = 该帧执行后的帧号）。同槽旧快照被覆写。</summary>
        public void Capture(int frame, in SimWorldState s)
        {
            int slot = frame % _capacity;
            s.CopyTo(_slots[slot]);
            _frames[slot] = frame;
        }

        /// <summary>环窗口内则恢复（CopyTo 回活状态）；越界返回 false。</summary>
        public bool TryRestore(int frame, SimWorldState s)
        {
            if (!ContainsFrame(frame)) return false;
            _slots[frame % _capacity].CopyTo(s);
            return true;
        }

        /// <summary>帧号是否仍在环窗口内（回滚深度判定入口）。</summary>
        public bool ContainsFrame(int frame)
        {
            if (frame < 0) return false;
            return _frames[frame % _capacity] == frame;
        }
    }
}
