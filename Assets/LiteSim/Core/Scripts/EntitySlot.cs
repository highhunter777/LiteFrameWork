namespace LiteSim
{
    /// <summary>
    /// 实体槽位（《状态同步实施方案》§3.1）：纯值类型（blittable）——
    /// EntitySlot[] 才能被 Array.Copy 整块深拷（#1/#4）。
    /// 自定义状态不放本 struct（#2：struct 内放数组字段会被浅拷共享，快照必错），
    /// 一律落 SimWorldState.CustomData 平面数组（slot * CustomBytesPerEntity + offset 寻址）。
    /// </summary>
    public partial struct EntitySlot
    {
        /// <summary>
        /// 稳定 Id：低 16 位 slotIndex | 高 48 位 version（#7 防重绕——16 位 version 在弹幕密集时
        /// 约 20 分钟耗尽）。由 SimWorldState 分配器组装，业务不可手改；0 = 无效（空槽/分配失败）。
        /// </summary>
        [StateLayer(StateLayer.Public)]
        public long Id;

        /// <summary>位置（y 轴 = 2.5D 向上，§3.5）。</summary>
        [StateLayer(StateLayer.Public, flatten: new[] { "X", "Y", "Z" })]
        public SimVector3 Pos;

        /// <summary>速度。</summary>
        [StateLayer(StateLayer.Public, flatten: new[] { "X", "Y", "Z" })]
        public SimVector3 Vel;

        /// <summary>朝向（XZ 平面，弧度；射击必需）。</summary>
        [StateLayer(StateLayer.Public)]
        public float Yaw;

        [StateLayer(StateLayer.Public)]
        public int Hp;

        /// <summary>标志位（位定义见 <see cref="EntityFlags"/>；活体判定以 AliveBitmap 为准）。
        /// **随公共快照 / 差分器 / <c>SimChecksum</c> / <c>SlotDelta.flags</c> 全链下发**——远端可见。</summary>
        [StateLayer(StateLayer.Public)]
        public uint Flags;

        // ---- P0 公共战斗面（《游戏业务系统总设计》§1/《状态同步专项设计》§5.2 PublicStateSnapshot）----
        // 全部随公共 SlotDelta 下发：差分器逐字段比对，变化即增量发送。
        // 私有面（弹药/技能 CD/背包/资源）不在此——见 SimCombatRuntime/SimMatchRuntime 各自注释。

        /// <summary>护盾（公开投影值；明细在 <see cref="StatusSlotData"/>，P1 StatusSystem 维护同步）。</summary>
        [StateLayer(StateLayer.Public)]
        public int Shield;

        /// <summary>击杀数（公开：比分/KDA）。</summary>
        [StateLayer(StateLayer.Public)]
        public int Kills;

        /// <summary>死亡数（公开：比分/KDA）。</summary>
        [StateLayer(StateLayer.Public)]
        public int Deaths;

        /// <summary>当前装备武器槽（公开：-1 = 未装备；0..<see cref="SimConfig.WeaponSlotsPerEntity"/>-1）。
        /// 其他玩家渲染武器外观用它；弹药等私有运行态只在本人 PrivateStateSnapshot。</summary>
        [StateLayer(StateLayer.Public)]
        public int SelectedWeapon;

        /// <summary>
        /// 开火驻留窗剩余（逻辑帧数；0 = 不在开火态）——**Sim 权威开火态**（口径见
        /// 《角色状态与动作专项设计》§7 限速行）。
        /// **已公共化**（原为私有面，后经协议加列根治）：进公共快照（<c>fire_stance_frames=19</c>）
        /// **且**进公共口径 checksum（见 <see cref="SimChecksum.ComputePublicChecksum"/>）——
        /// 该字段改写 Vel/Yaw（限速+朝准星语境），客户端回滚重放必须能从快照重建，
        /// 否则窗内限速/朝向分叉 ⇒ 逐快照纠偏（橡皮筋）。
        /// 置窗＝<see cref="ShootingSystem"/> 开火判定点（与 <see cref="FrameEventKind.Fire"/> 事件同点，
        /// 事件刷新制——每次判定重置满窗，上限即窗长）；递减＝<see cref="InputSystem"/> 每帧统一推进
        /// （全槽位含死亡/缺席——整数计数 ⇒ 确定性）；限速＝InputSystem 按"瞄准 ∨ 开火态"限到
        /// <see cref="CombatConfig.AimMoveSpeed"/>（走路档）。窗长单源 <see cref="CombatConfig.FireStanceFrames"/>
        /// （1s @60Hz，与开火动画时长解耦）。
        /// </summary>
        [StateLayer(StateLayer.Public, wireType: "uint32")]
        public byte FireStanceFrames;

        /// <summary>
        /// 离场转向中（0 = 否；1 = 武装/进行中）——债 #4 修复的**过渡状态**：
        /// 任意射击语境帧（瞄准/开火/窗内朝准星）**武装**；语境解除后的移动帧按
        /// <see cref="CombatConfig.FaceTurnRadPerSec"/> 逐帧转向移动方向（窗尽回转不瞬切），到位清零
        /// （后续移动帧恢复即时跟向）。
        /// **仅确定性内部态**：由输入历史派生（重放可重建），故**不上 wire、不占协议字段号**；
        /// 但参与 Sim 判定 ⇒ 进全量口径 checksum（重放对账必须逐位一致）。
        /// 与已公共化的 <see cref="FireStanceFrames"/> 不同类（后者改写限速判定，客户端必须重建）。
        /// 不上 wire ⇒ 无需 wireType（checksum 按本字段的 byte 折，与手写版一致）。
        /// </summary>
        [StateLayer(StateLayer.Internal)]
        public byte FaceExitTurning;

        /// <summary>
        /// 尸体期剩余（帧，0 = 不在尸体期）：死亡帧（Hp 跨线）由 <see cref="DamageSystem"/> 置满
        /// <see cref="CombatConfig.CorpseFrames"/>，<see cref="CleanupSystem"/> 逐帧递减、归零才回收槽位——
        /// 死亡表现（尸体动画/掉落/重连可见）的**权威载体窗**。期内零交互：InputSystem 输入作废、
        /// ShootingSystem 不可命中/不开火。**公共面**（尸体表现按快照重建——远端可见），
        /// 进公共快照（<c>corpse_frames=20</c>）与双口径 checksum（同 FireStanceFrames 先例）。
        /// </summary>
        [StateLayer(StateLayer.Public, wireType: "uint32")]
        public byte CorpseFrames;

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

        // ---- 分型 kind 位（《Sim组织专项设计》§2）----
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
