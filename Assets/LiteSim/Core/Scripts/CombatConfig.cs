namespace LiteSim
{
    /// <summary>
    /// 玩法数值单源（**手感参数与协议常量分离**——本类只装"一局战斗怎么打"，
    /// SimConfig 只装"确定性架构怎么搭"；两者生命周期不同：前者可调表迭代，后者编译期锁死）。
    ///
    /// - 默认值 = 灰盒实测值；
    /// - **Luban 表链路**：表源 `Luban/Data/#combatnum.xlsx` → `gen.bat` 产出客户端 bin
    ///   `Assets/GameData/Config/tbcombatnum.bytes`（双端同一份 bin，服务端直读不再有 json 产物）
    ///   → 启动装配调 <see cref="LoadFrom"/> 回填（客户端 `ConfigService`；服务端 `CombatNumbers`
    ///   装载链——**装载即回填**）。
    ///   **本类的默认值必须与表值一致**（L1 守卫用例 `CombatNumbersTests` 卡住漂移）；装载后两端同值（表数据进 buildHash，不一致直接拒进房）。
    /// - 确定性：全部 float/int 常量语义不变（位级确定的输入，无运算）。
    /// </summary>
    public static class CombatConfig
    {
        // ---- 移动 ----

        /// <summary>玩家移动速度（m/s，2.5D XZ 平面）。</summary>
        public static float MoveSpeed { get; private set; } = 5f;

        /// <summary>
        /// 瞄准态移速倍率（右键 ADS 期间移动上限 = <see cref="MoveSpeed"/> × 本值）。
        /// **取 0.5 = 乘 2 的幂**：位级精确、不引舍入（确定性纪律）。5 × 0.5 = **2.5 m/s**，
        /// 恰好等于视图 Walk 档上界（`LocomotionBlendMath.WalkFullMps`）——限速后"瞄准移动"
        /// 只需要 `AimWalk_*` 一套片段，不需要 AimJog。
        /// **表化待补**：数值表加列 `aim_move_speed` 后本常量让位（登记在《角色状态与动作专项设计》§7）。
        /// </summary>
        public const float AimMoveSpeedFactor = 0.5f;

        /// <summary>瞄准态移动速度上限（m/s）= <see cref="MoveSpeed"/> × <see cref="AimMoveSpeedFactor"/>。
        /// 参与 <see cref="CombatConfigDigest"/>——**联机身份**：两端不一致会以摘要不符当场拒进房。</summary>
        public static float AimMoveSpeed => MoveSpeed * AimMoveSpeedFactor;

        /// <summary>重力加速度（m/s²，y 轴向下，§3.5）。</summary>
        public static float Gravity { get; private set; } = -20f;

        /// <summary>**最大速度硬上限**（m/s，水平合速度）——服务器代码兜底（"配置只做软上限"）：
        /// 表值（走/跑/冲/滑铲/钩爪……MovementConfig）怎么调都是设计软值，
        /// 本护栏只对配置错误/增益叠加/未来机制 bug 生效，防实体被吹飞。取值盖过表内最快设计速度
        /// （钩爪拉拽 20）留 ~25% 余量；**刻意不进 digest**（代码常量两端编译期同值，无需摘要）。</summary>
        public const float HardMaxSpeed = 25f;

        // ---- 射击 ----

        /// <summary>hitscan 射程（m）。</summary>
        public static float HitscanRange { get; private set; } = 100f;

        /// <summary>
        /// 射击窗长（逻辑帧数）——**开火态时间**（**1s @60Hz = 60 帧独立常量**，
        /// 与开火动画时长解耦；事件刷新＝重置满窗，上限即窗长）。
        /// `EntitySlot.FireStanceFrames` 在 `ShootingSystem`
        /// 判定点置满、`InputSystem` 逐帧统一递减，并按"瞄准 ∨ 开火态"限速 `AimMoveSpeed`——进
        /// `CombatConfigDigest`（联机身份）。View 侧驻留窗按 `FireStanceFrames / SimConfig.TickRate`
        /// **同源派生**（改窗长只动此处）。**不变式：窗长 ≥ 开火片段播放时长**（防事件后窗先尽截断
        /// 在播射击片段——装配期校验，见 CharacterLocomotionDriver）。
        /// </summary>
        public const int FireStanceFrames = 60;

        /// <summary>
        /// 离场转向速率（rad/秒）——射击语境（瞄准 ∨ 开火帧 ∨ 窗内）解除后，朝向从准星转回移动方向
        /// **不瞬切**：按本速率逐帧过渡（债 #4"单点腰射朝向微摆"的根治）。
        /// 12 rad/s ≈ 687°/s：180° 回转 ≈0.26s、90° ≈0.13s——可见但不拖沓；射击语境内（含 ADS）
        /// 保持即时跟枪，不受本值影响。**代码常量（两端编译期同值）——与 <see cref="HardMaxSpeed"/>
        /// 同口径刻意不进 digest**（窗长进 digest 是因其表化计划 ⇒ 装载态漂移风险；本值无表化计划）。
        /// </summary>
        public const float FaceTurnRadPerSec = 12f;

        // ---- 逻辑枪口（子弹出射点＝本体+朝向系常量偏移——消激光/准心与弹道的原点残差）----

        /// <summary>
        /// 逻辑枪口·前向偏移（m，朝向系——射手 Yaw 前向）。子弹射线原点 = 本体 Pos + 前向×本值 +
        /// 右向×<see cref="MuzzleOffsetRight"/> + 高度<see cref="MuzzleOffsetHeight"/>。
        /// **为何不是"动画枪口"**：枪口位是表现层（服务器无模型/动画；客户端上报＝伪造面＋确定性破坏；
        /// 回溯无历史姿态）——固定常量偏移保留权威/确定/可回溯。取值 ≈ 瞄准态持枪的枪口位
        /// （视觉 <c>Weapon_Rifle/Muzzle</c> 锚点的近似）——观感校准项。
        /// **表化计划**：tb_weapon（G2-P1）落 per-weapon 列时迁表并经 <see cref="LoadFrom"/> 装载
        /// （故进 digest——同 <see cref="FireStanceFrames"/>"表化计划 ⇒ 进 digest"先例口径）。
        /// </summary>
        public const float MuzzleOffsetForward = 0.35f;

        /// <summary>逻辑枪口·右向偏移（m，朝向系右向 = forward 顺时针 90°——枪在右手侧）。</summary>
        public const float MuzzleOffsetRight = 0.2f;

        /// <summary>
        /// 逻辑枪口·高度（m，相对脚底）。**默认 = 眼高**——命中圆柱 y 带闸与爆头带判据
        /// （<see cref="HeadHitLine"/>）都以射线眼高为基准；默认值保持既有爆头/带闸口径不变（只挪 XZ）。
        /// </summary>
        public const float MuzzleOffsetHeight = 1f;

        /// <summary>
        /// 逻辑枪口世界位（**出射点单源**——<see cref="ShootingSystem"/> 与瞄准激光收敛端点共用同一实现，
        /// 防两处手写漂移）：本体 Pos + 朝向系常量偏移（前向×<see cref="MuzzleOffsetForward"/> +
        /// 右向×<see cref="MuzzleOffsetRight"/> + 高度<see cref="MuzzleOffsetHeight"/>）。
        /// 右向 = forward 顺时针 90°：forward=(cos,sin) ⇒ right=(sin,−cos)。
        /// </summary>
        public static SimVector3 MuzzleOrigin(in SimVector3 pos, float yaw)
        {
            float cy = SimTrig.Cos(yaw);
            float sy = SimTrig.Sin(yaw);
            return new SimVector3(
                pos.X + cy * MuzzleOffsetForward + sy * MuzzleOffsetRight,
                pos.Y + MuzzleOffsetHeight,
                pos.Z + sy * MuzzleOffsetForward - cy * MuzzleOffsetRight);
        }

        /// <summary>命中圆柱半径（m）。</summary>
        public static float HitscanRadius { get; private set; } = 0.5f;

        /// <summary>命中圆柱高度（m，区间 [Pos.Y, Pos.Y + Height]）。</summary>
        public static float HitscanHeight { get; private set; } = 2f;

        /// <summary>
        /// 爆头带线（m，相对目标脚底）：命中高度 ≥ 目标 Pos.Y + <see cref="HeadHitLine"/> 判爆头。
        /// 当前 hitscan 为**水平射线**（命中高度 = 射手眼高）——同地平面对枪（眼高 ≈ 半身高 1.0m）
        /// 永不达线，高差位（高台打低处）才有爆头；俯仰轴归输入面扩展（AimY 未实现）。
        /// **代码常量（两端编译期同值）——与 <see cref="FaceTurnRadPerSec"/> 同口径刻意不进 digest**
        /// （无表化计划；表化时改属性进 <see cref="LoadFrom"/> 并纳入 digest）。
        /// </summary>
        public const float HeadHitLine = 1.7f;   // = HitscanHeight(2f) × 0.85

        /// <summary>
        /// 爆头伤害倍率（移位数）：伤害 `&lt;&lt; HeadshotDamageShift`（×2^shift——位级精确，
        /// 与 AimMoveSpeed"乘 2 的幂"同约定）；0 = 无倍率。倍率在**命中判定处**应用——
        /// Damage 命令携带即最终值，结算侧无需知部位。**代码常量不进 digest**（表化随数值调参批）。
        /// </summary>
        public const int HeadshotDamageShift = 1;

        /// <summary>
        /// 尸体期（帧，@60Hz = 3s）：死亡跨线后槽位的保留期——尸体表现/掉落/重连可见的权威载体窗；
        /// 期内实体零交互（输入作废/不可命中/不开火），期满由 CleanupSystem 回收。
        /// **代码常量不进 digest**（表化随数值调参批——同 HeadshotDamageShift 口径）。
        /// </summary>
        public const int CorpseFrames = 180;

        // ---- 伤害 ----

        /// <summary>基础伤害（命中值 = BaseDamage ± DamageSpread 内浮动）。</summary>
        public static int BaseDamage { get; private set; } = 25;

        /// <summary>伤害浮动幅度（命中值 = Base + rng.NextRange(-Spread, Spread+1)；0 = 无浮动）。</summary>
        public static int DamageSpread { get; private set; } = 1;

        /// <summary>出生 HP（实体初始生命）。**表字段 `entity_hp`**（实体表化前并入本表：单一职责暂借位）。</summary>
        public static int EntityHp { get; private set; } = 100;

        /// <summary>
        /// Luban 表装载接缝：tb_combat_num 读取后调用，逐字段覆写；
        /// 参数未做合法性钳制（来源是策划表而非外部输入）。
        /// </summary>
        public static void LoadFrom(float moveSpeed, float gravity, float hitscanRange, float hitscanRadius,
            float hitscanHeight, int baseDamage, int damageSpread, int entityHp)
        {
            MoveSpeed = moveSpeed;
            Gravity = gravity;
            HitscanRange = hitscanRange;
            HitscanRadius = hitscanRadius;
            HitscanHeight = hitscanHeight;
            BaseDamage = baseDamage;
            DamageSpread = damageSpread;
            EntityHp = entityHp;
        }
    }
}
