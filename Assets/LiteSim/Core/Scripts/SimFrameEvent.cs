namespace LiteSim
{
    /// <summary>
    /// 帧事件（§3.7 帧事件缓冲）：Sim → View 的离散告知（命中/暴击/死亡/开火——状态差分推不出来的）。
    /// 消费时机是本设计的关键：每个逻辑帧 Step 结束后立即消费并清空（§3.7）——
    /// 追帧不丢事件、不必进快照、回滚重放天然正确。
    /// </summary>
    public struct FrameEvent
    {
        public FrameEventKind Kind;

        /// <summary>主体 Id（开火者/命中目标/死者）。</summary>
        public long EntityId;

        /// <summary>对象 Id（Hit=射手；Death=击杀者；Fire 无对象为 0）。</summary>
        public long OtherId;

        /// <summary>数值（伤害量等，按 Kind 释义）。</summary>
        public int Value;

        /// <summary>事件位置（Fire=射线原点；Hit=命中点；Death=死亡位置）。</summary>
        public SimVector3 Pos;
    }

    /// <summary>帧事件类型（§3.7）。当前产生 Fire/Hit/Death；Crit/Explosion 为后续里程碑预留。</summary>
    public enum FrameEventKind : byte
    {
        Fire = 0,
        Hit = 1,
        Death = 2,
        Crit = 3,
        Explosion = 4,
        Respawn = 5,
    }

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
