namespace LiteSim
{
    /// <summary>
    /// 玩法常量与数值读口（**手感参数与协议常量分离**——本类装"一局战斗怎么打"的编译期常量
    /// 与装载面读口；SimConfig 只装"确定性架构怎么搭"；两者生命周期不同：前者可调表迭代，后者编译期锁死）。
    ///
    /// **数值面已实例化（技术债 #1 根治）**：
    /// - 装载面 8 字段（移速/重力/射程/伤害/浮动/HP/瞄准限速档/射击窗长）住 <see cref="CombatValues"/> 实例——装载链
    ///   （客户端 `ConfigService`、服务端 `ServerTableLoader`）解析表产出实例，**经参数显式传入机制消费**
    ///   （`SimStep`→系统、房间快照），不再有"装载即回填全局"的顺序问题；
    /// - 本类的同名只读属性 = **客户端单世界读口**（`Publish` 原子替换）——机制消费不得读，
    ///   默认 = <see cref="CombatValues.Default"/>（必须与表值一致，L1 守卫卡漂移）；
    /// - **表化的演化路径不变**：新字段（如 per-weapon 枪口列）进表后经同一实例通道装载（进 digest——联机身份）。
    ///
    /// - **判定几何属装载面**：身位几何（body_radius/hitscan_height/hitscan_radius）、逻辑枪口三常量与爆头带
    ///   比例/半径都在表 `#combatgeom`（13 列），装载为 <see cref="CombatGeometry"/> 并随 <see cref="CombatValues"/>
    ///   按值传递；<see cref="Geometry"/> 只是展示投影（未发布时返回 <see cref="CombatValues.Default"/> 的几何）。
    /// - 确定性：全部 float/int 常量语义不变（位级确定的输入，无运算）。
    /// </summary>
    public static class CombatConfig
    {
        private static CombatValues _loaded = CombatValues.Default;
        private static bool _published;

        /// <summary>发布装载值（**客户端单世界读口**的原子实例替换；服务端**不经本槽**——装载产物经参数
        /// 显式传入 `HostAssembly`→`ServerHost`→`RoomRuntime`）。测试不依赖本槽——机制路径直接传实例。</summary>
        public static void Publish(in CombatValues values) { _loaded = values; _published = true; }

        /// <summary>当前装载值实例（判定读口；未发布时抛——避免把展示兜底当权威值）。
        /// **机制消费禁读**——Sim 一律经 `in CombatValues` 参数接收。</summary>
        public static CombatValues Loaded
        {
            get
            {
                if (!_published)
                    throw new System.InvalidOperationException("CombatConfig.Loaded 未发布：先 Publish/装载表");
                return _loaded;
            }
        }

        /// <summary>是否已发布（展示/工具可先问再读）。</summary>
        public static bool HasPublished => _published;

        /// <summary>尝试取已发布值（纯展示读者用；未发布返回 false，不抛）。</summary>
        public static bool TryGetLoaded(out CombatValues values)
        {
            values = _loaded;
            return _published;
        }

        /// <summary>非判定投影（展示兜底）：未发布返回 <see cref="CombatValues.Default"/> 的几何，不抛。
        /// 判定/烘焙写入源不得读本口。</summary>
        public static CombatGeometry Geometry => _published ? _loaded.Geometry : CombatValues.Default.Geometry;

        /// <summary>测试隔离：复位发布状态（internal + InternalsVisibleTo）。</summary>
        internal static void ResetForTests()
        {
            _loaded = CombatValues.Default;
            _published = false;
        }

        // ---- 移动（装载面——实例字段的读口投影） ----

        /// <summary>玩家移动速度（m/s，2.5D XZ 平面）。读写口见类头——机制消费读传入的 <see cref="CombatValues"/>。</summary>
        public static float MoveSpeed => _loaded.MoveSpeed;

        /// <summary>
        /// 瞄准态移动速度上限（m/s）= <see cref="CombatValues.AimMoveSpeed"/>（表值 tb_combatnum.aim_move_speed）。
        /// 所在的**判定面表**（tbcombatnum）参与 <see cref="CombatConfigDigest"/>——**联机身份**：
        /// 服务端随 `StartGame`/`ReconnectResponse` 下发房间摘要，客户端按本地装载表字节复算比对（批 E 已接）。
        ///
        /// **表化口径（勿按旧式派生推断）**：直接落最终速度（表列 aim_move_speed），不再由
        /// <see cref="MoveSpeed"/> × 倍率派生——旧倍率常量（0.5，取 2 的幂保位级精确）已退役：
        /// 直落最终值后无乘法、无舍入面。代价：不随 <see cref="MoveSpeed"/> 联动
        /// （口径见 CombatValues.AimMoveSpeed）。
        /// </summary>
        public static float AimMoveSpeed => _loaded.AimMoveSpeed;

        /// <summary>重力加速度（m/s²），来自 tbmovementconfig.gravity 的固定对局配置投影。读口见类头。</summary>
        public static float Gravity => _loaded.Gravity;

        // ---- 射击 ----

        /// <summary>hitscan 射程（m）。读口见类头。</summary>
        public static float HitscanRange => _loaded.HitscanRange;

        /// <summary>
        /// 射击窗长（逻辑帧数）——**开火态时间**（默认表值 1s @60Hz = 60 帧，
        /// 与开火动画时长解耦；事件刷新＝重置满窗，上限即窗长）。表值
        /// tb_combatnum.fire_stance_frames，落在 <see cref="CombatValues.FireStanceFrames"/>——
        /// **改窗长只动表**（本行是表现面读口；机制面读传入的实例）。
        /// `EntitySlot.FireStanceFrames` 在 `ShootingSystem`
        /// 判定点置满、`InputSystem` 逐帧统一递减，并按"瞄准 ∨ 开火态"限速 `AimMoveSpeed`——所在判定表
        /// （tbcombatnum）进 `CombatConfigDigest`（联机身份）。View 侧驻留窗按 `FireStanceFrames / SimConfig.TickRate`
        /// **同源派生**。**不变式：窗长 ≥ 开火片段播放时长**（防事件后窗先尽截断
        /// 在播射击片段——装配期校验，见 CharacterLocomotionDriver）；且须 1..255（落 byte，L1 守卫卡）。
        /// </summary>
        public static int FireStanceFrames => _loaded.FireStanceFrames;

        // ---- 伤害（装载面——实例字段的读口投影） ----

        /// <summary>基础伤害（命中值 = BaseDamage ± DamageSpread 内浮动）。读口见类头。</summary>
        public static int BaseDamage => _loaded.BaseDamage;

        /// <summary>伤害浮动幅度（命中值 = Base + rng.NextRange(-Spread, Spread+1)；0 = 无浮动）。读口见类头。</summary>
        public static int DamageSpread => _loaded.DamageSpread;

        /// <summary>出生 HP，来自 tbentityconfig.initial_hp 的固定对局配置投影。读口见类头。</summary>
        public static int EntityHp => _loaded.EntityHp;
    }
}
