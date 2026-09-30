namespace LiteSim
{
    /// <summary>
    /// 实体槽位（《状态同步实施方案》§3.1 + M8 决策 #3）：纯值类型（blittable）——
    /// EntitySlot[] 才能被 Array.Copy 整块深拷（#1/#4）。
    /// 自定义状态不放本 struct（#2：struct 内放数组字段会被浅拷共享，快照必错），
    /// 一律落 SimWorldState.CustomData 平面数组（slot * CustomBytesPerEntity + offset 寻址）。
    /// </summary>
    public struct EntitySlot
    {
        /// <summary>
        /// 稳定 Id：低 16 位 slotIndex | 高 48 位 version（#7 防重绕——16 位 version 在弹幕密集时
        /// 约 20 分钟耗尽）。由 SimWorldState 分配器组装，业务不可手改；0 = 无效（空槽/分配失败）。
        /// </summary>
        public long Id;

        /// <summary>位置（y 轴 = 2.5D 向上，§3.5）。</summary>
        public SimVector3 Pos;

        /// <summary>速度。</summary>
        public SimVector3 Vel;

        /// <summary>朝向（XZ 平面，弧度；射击必需）。</summary>
        public float Yaw;

        public int Hp;

        /// <summary>标志位（位定义见 <see cref="EntityFlags"/>；活体判定以 AliveBitmap 为准）。
        /// **随公共快照 / 差分器 / <c>SimChecksum</c> / <c>SlotDelta.flags</c> 全链下发**——远端可见。</summary>
        public uint Flags;

        // ---- P0 公共战斗面（《游戏业务系统总设计》§1/《状态同步专项设计》§5.2 PublicStateSnapshot）----
        // 全部随公共 SlotDelta 下发：差分器逐字段比对，变化即增量发送。
        // 私有面（弹药/技能 CD/背包/资源）不在此——见 SimCombatRuntime/SimMatchRuntime 各自注释。

        /// <summary>护盾（公开投影值；明细在 <see cref="StatusSlotData"/>，P1 StatusSystem 维护同步）。</summary>
        public int Shield;

        /// <summary>击杀数（公开：比分/KDA）。</summary>
        public int Kills;

        /// <summary>死亡数（公开：比分/KDA）。</summary>
        public int Deaths;

        /// <summary>当前装备武器槽（公开：-1 = 未装备；0..<see cref="SimConfig.WeaponSlotsPerEntity"/>-1）。
        /// 其他玩家渲染武器外观用它；弹药等私有运行态只在本人 PrivateStateSnapshot。</summary>
        public int SelectedWeapon;

    }

    /// <summary>
    /// <see cref="EntitySlot.Flags"/> 的位定义（**取位只能从这里来**）。
    ///
    /// 为什么单列一个类型：**输入位与实体标志位是两个位空间**——直接拿
    /// <c>SimInputFrame.ButtonAim</c> 当标志位用，日后输入位重排会静默改掉快照/checksum 语义
    /// （`Flags` 进 <c>SimChecksum</c> 与线上 `SlotDelta.flags`，跨端身份敏感）。值可以碰巧相同，
    /// 名字必须各自独立。
    /// </summary>
    public static class EntityFlags
    {
        /// <summary>瞄准态（右键 ADS；由 <c>InputSystem</c> 从输入位**每帧覆写**——连续状态，不是边沿）。</summary>
        public const uint Aiming = 1u << 0;

        // ---- 分型 kind 位（《实体分型表设计》§1，2026-09-29）----
        // kind 位 = "该槽位持有哪张分型表行数据"的迷你 archetype mask：置位 ⟺ Items/Projectiles/Zones
        // 对应行有效。**只增不改不重排**（Flags 进 SimChecksum 与线上 SlotDelta.flags——跨端身份敏感，
        // 与输入位空间的纪律同款）。

        /// <summary>地面道具（<see cref="SimWorldState.Items"/> 行有效）。</summary>
        public const uint KindItem = 1u << 1;

        /// <summary>投掷物（<see cref="SimWorldState.Projectiles"/> 行有效）。</summary>
        public const uint KindProjectile = 1u << 2;

        /// <summary>区域效果（<see cref="SimWorldState.Zones"/> 行有效）。</summary>
        public const uint KindZone = 1u << 3;
    }
}
