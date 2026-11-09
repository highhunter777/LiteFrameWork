namespace LiteSim
{
    /// <summary>
    /// 帧事件缓冲（§3.7）：与 CommandBuffer 同构，Capacity = 256；满则丢弃并计数。
    /// 可变结构体，字段不可 readonly（防御性拷贝丢写）；由 SimWorldState 构造期分配 Items。
    /// </summary>
    public struct FrameEventBuffer
    {
        /// <summary>容量。</summary>
        public const int Capacity = 256;

        public int Count;

        public FrameEvent[] Items;

        /// <summary>溢出丢弃累计（诊断用）。</summary>
        public int OverflowCount;

        public void Write(FrameEventKind kind, long entityId, long otherId, int value, SimVector3 pos)
        {
            if (Count >= Capacity)
            {
                OverflowCount++;
                return;
            }

            FrameEvent e;
            e.Kind = kind;
            e.EntityId = entityId;
            e.OtherId = otherId;
            e.Value = value;
            e.Pos = pos;
            Items[Count] = e;
            Count++;
        }

        /// <summary>每逻辑帧末消费后由驱动调用（不在 SimStep 内清）。</summary>
        public void Clear()
        {
            Count = 0;
        }
    }
}
