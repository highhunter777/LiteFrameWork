namespace LiteSim
{
    /// <summary>
    /// 单玩家一帧的输入面（瞄准存方向不存角度，见
    /// 《角色状态与动作实现设计》§7-1；**P0 动作意图扩展**：《游戏业务系统总设计》§3.2 输入位和边沿）。
    /// 采集侧（沙盒直接喂 / 输入服务 `LiteClient.IInputService`）组装；Sim 侧只消费。
    /// **瞄准存方向不存角度**：射击射线本就需要方向（省一次三角函数往返）、无角度环绕与量化边界问题；
    /// 朝向 `EntitySlot.Yaw` 降为 **Sim 内派生量**（InputSystem 经 `SimTrig.Atan2` 算，表现层仍需要朝向）。
    /// Step 收到的输入数组按 playerId 升序排列，数组顺序即处理顺序（§3.3 同帧多请求的确定性来源）。
    ///
    /// P0 边沿纪律（§3.2）：离散意图（Reload/Switch/Skill/Pickup/UseItem）只在按键沿所在的**一个逻辑帧**
    /// 置位并携带递增 <see cref="ActionSeq"/>；连续意图（Fire/移动/瞄准）按帧置位不带 seq。
    /// 渲染帧只更新待消费意图，逻辑帧只消费一次。
    /// </summary>
    public struct SimInputFrame
    {
        /// <summary>开火位（连续意图）。</summary>
        public const uint ButtonFire = 1u << 0;

        /// <summary>换弹（P0，离散意图——带 action_seq）。</summary>
        public const uint ButtonReload = 1u << 1;

        /// <summary>切枪（P0，离散意图——带 action_seq + selected_weapon_slot）。</summary>
        public const uint ButtonSwitchWeapon = 1u << 2;

        /// <summary>技能 1（P0，离散意图——带 action_seq + target_entity_id）。</summary>
        public const uint ButtonSkill1 = 1u << 3;

        /// <summary>技能 2（P0，离散意图）。</summary>
        public const uint ButtonSkill2 = 1u << 4;

        /// <summary>技能 3（P0，离散意图）。</summary>
        public const uint ButtonSkill3 = 1u << 5;

        /// <summary>拾取/交互（P0，离散意图——带 action_seq + target_entity_id）。</summary>
        public const uint ButtonPickup = 1u << 6;

        /// <summary>使用局内物品（P0，离散意图——快捷栏由局内背包 quick_slot 绑定解析）。</summary>
        public const uint ButtonUseItem = 1u << 7;

        /// <summary>闪避/保留位（《游戏业务系统总设计》§3.2 位 8：已定义未实现——服务器白名单放行，Sim 侧暂无消费者）。</summary>
        public const uint ButtonDodge = 1u << 8;

        /// <summary>瞄准（ADS，连续意图——长按右键期间每帧置位，松开清零；不带 action_seq）。</summary>
        public const uint ButtonAim = 1u << 9;

        /// <summary>离散意图位集（需携带 action_seq 的按钮）。</summary>
        public const uint DiscreteIntentButtons =
            ButtonReload | ButtonSwitchWeapon | ButtonSkill1 | ButtonSkill2 | ButtonSkill3 | ButtonPickup | ButtonUseItem;

        /// <summary>
        /// **预测帧保留位集**（<c>RollbackSim</c> 的"沿用上一帧"口径）：连续意图可预测（与移动同性质），
        /// 开火与离散意图不预测（猜错代价极大）。**新增连续位时改这里**——不要在回滚代码里散写位常量，
        /// 否则再加位必漏。
        /// </summary>
        public const uint PredictedButtons = ButtonAim;

        /// <summary>
        /// **服务器回溯专用**：本槽输入表示"该实体在回溯帧上补判一次开火"。
        /// 不是客户端能上报的按键位——传输层把 <c>Buttons</c> 限制在已定义的玩家按键集合（见 InputGate），
        /// 服务器内部构造的补判输入才带此位；<see cref="ShootingSystem"/> 据此跳过移动向量归一化、
        /// 并跳过"武器冷却/弹药"类前置（当前无此状态，先落下契约以免后续加武器系统时漏改）。
        /// </summary>
        public const uint ButtonFireFlag = 1u << 31;

        /// <summary>玩家实体 Id（消费方经 TryResolve 定位；失效 = 目标已死，本帧输入丢弃）。</summary>
        public long EntityId;

        /// <summary>移动向量 X（长度 ≤ 1 由采集侧保证）。</summary>
        public float MoveX;

        /// <summary>移动向量 Z（2.5D：XZ 平面移动）。</summary>
        public float MoveZ;

        /// <summary>瞄准方向 X（XZ 平面）——**开火帧必须为非零向量**，长度 ≤ 1 由采集侧保证。
        /// 与移动正交（朝向与相机解耦，《联机Demo设计》§13）；Sim 只消费、不解释来源。</summary>
        public float AimX;

        /// <summary>瞄准方向 Z（长度 ≤ 1 由采集侧保证）。</summary>
        public float AimZ;

        /// <summary>
        /// 瞄准方向 Y（三维化——俯视角爆头判定用；长度 ≤ 1 由采集侧保证）。
        /// 与 <see cref="AimX"/>/<see cref="AimZ"/> 同约束（单位向量三分量）；**0 = 水平射线**
        /// （等价旧 2.5D 行为，既有二维调用方不传即保持现状）。
        ///
        /// **背景（《俯视角三维命中与爆头判定专项设计》§1/§3）**：旧口径瞄准由「屏幕点 → 相机射线
        /// → ∩地面平面 → 只取 XZ」得出，**Y 分量恒为 0** ⇒ 出射线恒水平（眼高 1.0m）⇒ 平地
        /// 永远够不到爆头线 1.7m。三维化后 Aim 含真实 Y：玩家抬高准心 ⇒ 射线仰角变化 ⇒ 远处目标
        /// 处射线抬高 ⇒ 可命中头部带（借鉴《逃离鸭科夫》「准心在头上就爆头」的部位判定口径）。
        ///
        /// **不进 <see cref="SimChecksum"/>**：瞄准向量属**输入面**（随 <c>InputHistory</c> 逐帧记录
        /// 供回滚重放），与「世界状态」是两套——checksum 只覆盖 <c>SimWorldState</c>
        /// （Frame/槽位/Globals/…），**不含输入帧**。故本字段无须重录 checksum 基线；
        /// 协议侧仍须两端同帧同值（《状态同步专项设计》输入面纪律）。
        /// </summary>
        public float AimY;

        /// <summary>按键位集（位定义见上；服务器按白名单过滤未定义位）。</summary>
        public uint Buttons;

        // ---- P0 动作意图面（《状态同步专项设计》§5.1 追加；《游戏业务系统总设计》§3.2）----

        /// <summary>SwitchWeapon 的目标武器槽（0..<see cref="SimConfig.WeaponSlotsPerEntity"/>-1）。
        /// 仅 Buttons 含 <see cref="ButtonSwitchWeapon"/> 时有效；服务器传输层做范围校验。</summary>
        public int SelectedWeaponSlot;

        /// <summary>意图目标实体 Id（Pickup 的拾取物 / Skill 的指向目标等；0 = 无目标）。
        /// 数值合法性（是否解析得到活体）归 Sim 语义层——失效即意图作废，正常路径。</summary>
        public long TargetEntityId;

        /// <summary>离散动作请求序号（逐玩家单调递增，uint；服务器按 (playerId, seq) 去重防重放/迟到重放）。
        /// 只有携带 <see cref="DiscreteIntentButtons"/> 的帧要求非零。</summary>
        public uint ActionSeq;
    }
}
