namespace LiteSim
{
    /// <summary>
    /// 玩法常量与数值读口（**手感参数与协议常量分离**——本类装"一局战斗怎么打"的编译期常量
    /// 与装载面读口；SimConfig 只装"确定性架构怎么搭"；两者生命周期不同：前者可调表迭代，后者编译期锁死）。
    ///
    /// **数值面已实例化（技术债 #1 根治）**：
    /// - 装载面 6 字段（移速/重力/射程/伤害/浮动/HP）住 <see cref="CombatValues"/> 实例——装载链
    ///   （客户端 `ConfigService`、服务端 `CombatNumbers`）解析表产出实例，**经参数显式传入机制消费**
    ///   （`SimStep`→系统、房间快照），不再有"装载即回填全局"的顺序问题；
    /// - 本类的同名只读属性 = **客户端单世界读口**（`Publish` 原子替换）——机制消费不得读，
    ///   默认 = <see cref="CombatValues.Default"/>（必须与表值一致，L1 守卫卡漂移）；
    /// - **表化的演化路径不变**：新字段（如 per-weapon 枪口列）进表后经同一实例通道装载（进 digest——联机身份）。
    ///
    /// - **烘焙/导出常量不属装载面**：身位几何（HitscanRadius/HitscanHeight）、逻辑枪口三常量与爆头带比例（HeadBake）
    ///   为烘焙/导出常量（`BodyBake.g.cs`/`MuzzleBake.g.cs`/`HeadBake.g.cs`，工具重烘）——不进表、不进实例，
    ///   两端同值由同一份生成文件保证。
    /// - 确定性：全部 float/int 常量语义不变（位级确定的输入，无运算）。
    /// </summary>
    public static class CombatConfig
    {
        private static CombatValues _loaded = CombatValues.Default;

        /// <summary>发布装载值（**客户端单世界读口**的原子实例替换；服务端**不经本槽**——装载产物经参数
        /// 显式传入 `HostAssembly`→`ServerHost`→`RoomRuntime`）。测试不依赖本槽——机制路径直接传实例。</summary>
        public static void Publish(in CombatValues values) { _loaded = values; }

        /// <summary>当前装载值实例（表现面读口；**机制消费禁读**——Sim 一律经 `in CombatValues` 参数接收）。</summary>
        public static CombatValues Loaded => _loaded;

        // ---- 移动（装载面——实例字段的读口投影） ----

        /// <summary>玩家移动速度（m/s，2.5D XZ 平面）。读写口见类头——机制消费读传入的 <see cref="CombatValues"/>。</summary>
        public static float MoveSpeed => _loaded.MoveSpeed;

        /// <summary>
        /// 瞄准态移速倍率（右键 ADS 期间移动上限 = <see cref="CombatValues.MoveSpeed"/> × 本值）。
        /// **取 0.5 = 乘 2 的幂**：位级精确、不引舍入（确定性纪律）。5 × 0.5 = **2.5 m/s**，
        /// 恰好等于视图 Walk 档上界（`LocomotionBlendMath.WalkFullMps`）——限速后"瞄准移动"
        /// 只需要 `AimWalk_*` 一套片段，不需要 AimJog。
        /// **表化待补**：数值表加列 `aim_move_speed` 后本常量让位（登记在《角色状态与动作专项设计》§7）。
        /// </summary>
        public const float AimMoveSpeedFactor = 0.5f;

        /// <summary>瞄准态移动速度上限（m/s）= <see cref="CombatValues.AimMoveSpeed"/>（实例派生）。
        /// 参与 <see cref="CombatConfigDigest"/>——**联机身份**：两端不一致会以摘要不符当场拒进房。</summary>
        public static float AimMoveSpeed => _loaded.AimMoveSpeed;

        /// <summary>重力加速度（m/s²，y 轴向下，§3.5）。读口见类头。</summary>
        public static float Gravity => _loaded.Gravity;

        /// <summary>**最大速度硬上限**（m/s，水平合速度）——服务器代码兜底（"配置只做软上限"）：
        /// 表值（走/跑/冲/滑铲/钩爪……<see cref="MovementValues"/>）怎么调都是设计软值，
        /// 本护栏只对配置错误/增益叠加/未来机制 bug 生效，防实体被吹飞。取值盖过表内最快设计速度
        /// （钩爪拉拽 20）留 ~25% 余量；**刻意不进 digest**（代码常量两端编译期同值，无需摘要）。</summary>
        public const float HardMaxSpeed = 25f;

        // ---- 射击 ----

        /// <summary>hitscan 射程（m）。读口见类头。</summary>
        public static float HitscanRange => _loaded.HitscanRange;

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
        // 三常量 = **烘焙值**（`MuzzleBake.g.cs`：采样 prefab 的 `Weapon_Rifle/Muzzle` 锚点 @ AimIdle t=0；
        // 重烘 = Editor 工具 `MuzzleOffsetBaker`，美术调锚点/换枪后重跑）。
        // **为何不是"动画枪口"**：枪口位是表现层（服务器无模型/动画；客户端上报＝伪造面＋确定性破坏；
        // 回溯无历史姿态）——**静态烘焙常量**保留权威/确定/可回溯。参考姿态选 AimIdle 的单套依据：
        // 射弹时刻视觉姿态单族（瞄准 = AimIdle；腰射开火窗 FireIdle 也以 AimIdle 循环填窗
        // ——`CombatGirlsAnimationProfile`/`CombatAnimMachine`）。**表化计划**：tb_weapon（G2-P1）落
        // per-weapon 列时迁表并经实例通道装载（故进 digest——同 FireStanceFrames 先例）。

        /// <summary>逻辑枪口·前向偏移（m，朝向系——烘焙：prefab Weapon_Rifle/Muzzle 锚点 @ AimIdle t=0）。</summary>
        public const float MuzzleOffsetForward = MuzzleBake.Forward;

        /// <summary>逻辑枪口·右向偏移（m，朝向系右向 = forward 顺时针 90°——同上烘焙值）。</summary>
        public const float MuzzleOffsetRight = MuzzleBake.Right;

        /// <summary>
        /// 逻辑枪口·高度（m，相对脚底——同上烘焙值）。**由旧"眼高 1.0"常量改为瞄准持枪枪口实高**：
        /// 命中圆柱 Y 带闸与水平弹道几何以射线原点高度为基准，本值抬高后"高差位水平弹道"的
        /// 擦顶特例几何随之变化（登记于施工记录；爆头判定不受影响——判据走 AimPoint.Y）。
        /// </summary>
        public const float MuzzleOffsetHeight = MuzzleBake.Height;

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

        /// <summary>
        /// **命中柱半径（m）——判定宽容裁决常量**："所见即所判"——准心落在视觉角色身上即应可命中。
        /// 战斗姿态视觉轮廓实测（BakeMesh 真变形、离轴最大半径）：下半身 0.27~0.43、上半身 0.6~0.8
        /// （手臂/持枪/长发）；取 **0.45** 覆盖肢体与站姿、**放过**枪尖与长发尾（装饰性凸出物不追）。
        /// 消费：命中判定（<see cref="SimRaycast"/>）、爆头 AimPoint 归属校验（<see cref="ShootingSystem"/>）。
        /// **与物理半径 <see cref="BodyRadius"/> 解耦**（后者烘焙自 prefab CC、供移动去穿插与视图 CC）——
        /// 判定宽容不与物理体宽绑定。表化计划：恢复 tb_combat_num.hitscan_radius 列时迁表（进 digest）。
        /// </summary>
        public const float HitscanRadius = 0.45f;

        /// <summary>
        /// **物理半径（m）——烘焙值**（= prefab CharacterController 半径；`BodyBake.g.cs`，
        /// 重烘 = Editor 工具 `BodyCylinderBaker`）。消费：移动去穿插（<see cref="MovementSystem"/>）、
        /// 本地视图 CC（prefab 实值）。**不是命中柱**——命中宽容见 <see cref="HitscanRadius"/>。
        /// </summary>
        public const float BodyRadius = BodyBake.Radius;

        /// <summary>命中圆柱高度（m，区间 [Pos.Y, Pos.Y + Height]）——**烘焙值**（prefab CC height；
        /// 物理与命中同高：站姿与判定体上沿一致）；头部带下沿按本值比例派生。</summary>
        public const float HitscanHeight = BodyBake.Height;

        /// <summary>
        /// 爆头带线（m，相对目标脚底）——**头部带的下沿**：命中点相对高度 ≥ 本值判爆头。
        /// 上沿由 <see cref="HitscanHeight"/> 承担（<see cref="SimRaycast"/> 的 Y 带闸把越过头顶的命中
        /// 判为未命中）⇒ 头部带 = <c>[HeadHitLine, HitscanHeight]</c>，是**区间**而非半空间
        /// （《固定斜视角射击方案专项设计》§5）。
        ///
        /// **三维化后平地可爆头**：弹道方向由「逻辑枪口 → AimPoint」解出（<see cref="SimInputFrame.AimPointY"/>
        /// 携带准心射线命中点高度——采集侧瞄准点解算，《固定斜视角射击方案专项设计》§3），命中点高度随
        /// 弹道抬起 ⇒ 同地平面对枪把准心置于目标头部带即爆头。旧二维口径（射线恒水平、命中高度 = 射手眼高
        /// 1.0m）永远够不到本线。
        ///
        /// **取值 = <see cref="HitscanHeight"/> × <see cref="HeadBake.Ratio"/>（比例单源）**：随烘焙身高自动缩放
        /// （身高 1.8 × 0.775 ⇒ 头带 [1.395, 1.8]，高 0.405m）。俯视角可瞄性考量沿用旧口径：第一人称的 0.3m 窗口
        /// 在俯视角下屏幕像素太少（"框画出来了但打不中"），按身高比例留带仍明显小于躯干
        /// （爆头应是少数 rewarded 的高难度命中）。调它零副作用：不进 digest，改它不触发两端拒进房。
        /// **代码常量（两端编译期同值）——与 <see cref="FaceTurnRadPerSec"/> 同口径刻意不进 digest**；
        /// 比例的重调 = 工具 <c>HeadHitLineTuner</c>（编辑器拖带 / 测试模式滑杆）导出 <see cref="HeadBake"/>。
        /// </summary>
        public const float HeadHitLine = HitscanHeight * HeadBake.Ratio;

#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
        /// <summary>
        /// **测试模式实时预览覆写**（爆头线下沿，世界 Y；&lt; 0 = 不覆写）：对局内经测试面板滑杆实时调带——
        /// 判定（<see cref="ShootingSystem"/>）与 F11 身位绘制同读 <see cref="HeadHitLineLive"/>，
        /// 本地服与预测同进程同值，滑杆一动即见 Crit 档变化。**仅开发三宏内存在**（release 随宏编译剥离）；
        /// 定型值经工具导出 <see cref="HeadBake"/>——覆写不落盘、不进 digest/快照（测试沙箱件，与免死/缩放同性质）；
        /// 对局中改动后回滚重放按当帧值重判（沙箱可接受），复位按钮置回 -1。
        /// </summary>
        public static float HeadHitLineDevOverride = -1f;

        /// <summary>爆头线下沿的**实时取值**：测试模式覆写优先，否则烘焙比例派生值。</summary>
        public static float HeadHitLineLive => HeadHitLineDevOverride >= 0f ? HeadHitLineDevOverride : HeadHitLine;
#else
        /// <summary>爆头线下沿的**实时取值**（release：恒 = <see cref="HeadHitLine"/>——覆写面随三宏编译剥离）。</summary>
        public static float HeadHitLineLive => HeadHitLine;
#endif

        /// <summary>
        /// **爆头柱半径（m）——双柱判定几何的上段柱径**（窄于身体柱 <see cref="HitscanRadius"/>，
        /// "爆头柱/非爆头柱"裁决）：实体命中形状 = 身体柱 <c>[0, HeadHitLine) × HitscanRadius</c> ＋
        /// 爆头柱 <c>[HeadHitLine, HitscanHeight] × 本值</c> 的竖直堆叠（<see cref="SimRaycast"/> 求交单源）。
        /// **为什么是命中几何而不是事后水平闸**：AimPoint 是相机射线 ∩ 命中形状的表面点——单柱下瞄头的
        /// 射线在宽柱面取点（水平距恒 ≈柱径），任何事后闸都会拒掉合法瞄头点（实测"瞄头白字"）；
        /// 几何化后"命中爆头柱"即 Crit（<see cref="ShootingSystem"/> 按命中高度 ≥ 下沿判定，无水平闸）。
        /// 头部可见轮廓 ≈0.28~0.35（帽 0.283/发冠）；上段 [本值, HitscanRadius) 的环状空隙按裁决不可命中。
        /// **进 digest**（命中几何常量两端联机身份）；重调 = 调带工具导出 <see cref="HeadBake.Radius"/>
        /// （编辑器滑杆 / 测试模式实时覆写同导出口径）。
        /// </summary>
        public const float HeadshotRadius = HeadBake.Radius;

#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
        /// <summary>测试模式实时预览覆写（爆头柱半径；&lt; 0 = 不覆写）——与 <see cref="HeadHitLineDevOverride"/> 同口径。</summary>
        public static float HeadshotRadiusDevOverride = -1f;

        /// <summary>爆头柱半径的**实时取值**：测试模式覆写优先，否则导出值。</summary>
        public static float HeadshotRadiusLive => HeadshotRadiusDevOverride >= 0f ? HeadshotRadiusDevOverride : HeadshotRadius;
#else
        /// <summary>爆头柱半径的**实时取值**（release：恒 = <see cref="HeadshotRadius"/>——覆写面随三宏编译剥离）。</summary>
        public static float HeadshotRadiusLive => HeadshotRadius;
#endif

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

        // ---- 伤害（装载面——实例字段的读口投影） ----

        /// <summary>基础伤害（命中值 = BaseDamage ± DamageSpread 内浮动）。读口见类头。</summary>
        public static int BaseDamage => _loaded.BaseDamage;

        /// <summary>伤害浮动幅度（命中值 = Base + rng.NextRange(-Spread, Spread+1)；0 = 无浮动）。读口见类头。</summary>
        public static int DamageSpread => _loaded.DamageSpread;

        /// <summary>出生 HP（实体初始生命）。**表字段 `entity_hp`**（实体表化前并入本表：单一职责暂借位）。读口见类头。</summary>
        public static int EntityHp => _loaded.EntityHp;
    }
}
