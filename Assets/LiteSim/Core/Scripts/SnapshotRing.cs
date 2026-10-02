namespace LiteSim
{
    /// <summary>
    /// 快照环形缓冲（《状态同步实施方案》§5.2）：
    /// 每槽**独立 SimWorldState 实例**（构造期预分配，运行期零 new），Capture/Restore 全走 CopyTo
    /// （纯托管——不引 unsafe/memcpy；设计稿的 stateSize 参数取消，状态尺寸自述）。
    ///
    /// 帧号语义：Capture 在每帧 Step **后**调用，标记"该帧执行后状态"——
    /// TryRestore(F-1) 即"F 帧执行前状态"，与 §5.4 回滚伪码（target=F-1，重放 f=F..last）自洽。
    ///
    /// 容量 ctor 参数化：客户端回滚环 = MaxRollbackFrames+1 = 9；
    /// 服务端回溯环复用同一个类、容量 = LagCompHistory（16）——不做第二个实现。
    ///
    /// 越界语义：帧号不在环窗口内 → TryRestore 返回 false（超出深度正确退化，由上层停预测兜底）。
    /// 窗口判定：槽位帧号精确等于请求帧才算在环内（被覆写的旧帧自动视为逐出）。
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
