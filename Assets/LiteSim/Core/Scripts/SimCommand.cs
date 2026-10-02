namespace LiteSim
{
    /// <summary>
    /// 当帧延迟命令（§3.7 命令缓冲）：伤害 → 死亡 → 掉落这类"当帧内先后依赖"走此缓冲，
    /// 由 SimStep.FlushCommands 按固定轮次（最多 3 轮）分批结算——轮次固定才是确定的。
    /// 目标/来源用 64 位实体 Id（§3.1、§3.7），
    /// 消费方必须经 SimWorldState.TryResolve 校验（失效 = 目标已死，正常路径）。
    /// </summary>
    public struct SimCommand
    {
        public SimCommandKind Kind;

        /// <summary>目标实体 Id（Damage=受害者；Kill=被击杀者）。</summary>
        public long Target;

        /// <summary>来源实体 Id（伤害来源/击杀者；无来源为 0）。</summary>
        public long Source;

        /// <summary>数值（伤害量/分值/掉落表索引，按 Kind 释义）。</summary>
        public int Amount;
    }

    /// <summary>命令类型（§3.7）。当前产生 Damage/Kill；SpawnPickup/AddScore 为后续里程碑预留（类型位已冻结）。</summary>
    public enum SimCommandKind : byte
    {
        Damage = 0,
        Kill = 1,
        SpawnPickup = 2,
        AddScore = 3,
    }

    /// <summary>
    /// 命令缓冲（§3.7）：定长预分配、零 GC；满则丢弃并计数（<see cref="OverflowCount"/>），
    /// 不扩容——扩容 = 运行期分配。Core 零依赖不打日志，由驱动/SimSandbox 读计数上抛告警。
    /// 注意：可变结构体；SimWorldState 持有的字段不可声明 readonly（防御性拷贝会丢写）。
    /// </summary>
    public struct CommandBuffer
    {
        /// <summary>容量（#13）。</summary>
        public const int Capacity = 512;

        /// <summary>有效命令数（消费方按 [0, Count) 迭代 Items）。</summary>
        public int Count;

        /// <summary>定长命令数组（由 SimWorldState 构造期分配）。</summary>
        public SimCommand[] Items;

        /// <summary>溢出丢弃累计（诊断用；改容量常量而非扩容，见 §7 风险 4）。</summary>
        public int OverflowCount;

        public void Write(SimCommandKind kind, long target, long source, int amount)
        {
            if (Count >= Capacity)
            {
                OverflowCount++;
                return;
            }

            SimCommand cmd;
            cmd.Kind = kind;
            cmd.Target = target;
            cmd.Source = source;
            cmd.Amount = amount;
            Items[Count] = cmd;
            Count++;
        }

        /// <summary>每逻辑帧末由驱动调用（消费在 SimStep.FlushCommands 的固定轮次内完成）。</summary>
        public void Clear()
        {
            Count = 0;
        }
    }
}
