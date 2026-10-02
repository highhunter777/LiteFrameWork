namespace LiteSim
{
    /// <summary>
    /// 活体槽位零分配遍历器（§3.1/§3.8-借鉴2）：ref struct + ref 返回，
    /// 按槽位下标升序跳空槽——顺序恒定即确定性。不长为查询语言，只消除手写样板。
    /// ref struct 约束：不得装箱/进闭包/做字段（§7 风险 2），只能就地遍历。
    /// 用法：foreach (ref EntitySlot e in state.IterateAlive()) { ... }（沙盒/系统内）。
    /// </summary>
    public ref struct EntityIt
    {
        private readonly SimWorldState _state;
        private int _index;

        internal EntityIt(SimWorldState state)
        {
            _state = state;
            _index = -1;
        }

        /// <summary>当前活体槽（按引用返回；勿把该引用存过下一次 MoveNext）。</summary>
        public ref EntitySlot Current
        {
            get { return ref _state.Entities[_index]; }
        }

        public bool MoveNext()
        {
            for (_index = _index + 1; _index < SimConfig.MaxEntities; _index++)
            {
                if (_state.IsAlive(_index)) return true;
            }
            return false;
        }

        /// <summary>自枚举（foreach 模式）。</summary>
        public EntityIt GetEnumerator()
        {
            return this;
        }
    }
}
