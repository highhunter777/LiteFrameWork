namespace LiteSim
{
    /// <summary>
    /// 玩法常量与数值读口（**手感参数与协议常量分离**——本类装"一局战斗怎么打"的编译期常量 与装载面读口；SimConfig 只装"确定性架构怎么搭"；两者生命周期不同：前者可调表迭代，后者编译期锁死）。
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
        /// </summary>
        public static float AimMoveSpeed => _loaded.AimMoveSpeed;

        /// <summary>重力加速度（m/s²），来自 tbmovementconfig.gravity 的固定对局配置投影。读口见类头。</summary>
        public static float Gravity => _loaded.Gravity;

        // ---- 射击 ----

        /// <summary>hitscan 射程（m）。读口见类头。</summary>
        public static float HitscanRange => _loaded.HitscanRange;

        /// <summary>
        /// 射击窗长（逻辑帧数）——**开火态时间**（默认表值 1s @60Hz = 60 帧， 与开火动画时长解耦；事件刷新＝重置满窗，上限即窗长）。
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
