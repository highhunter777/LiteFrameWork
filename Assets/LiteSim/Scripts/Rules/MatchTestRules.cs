namespace LiteSim
{
    /// <summary>
    /// 每局测试规则持有件（**可变引用**）：免死/无限子弹与爆头带试验覆写的**运行态单源**——确定性系统
    /// （<see cref="DamageSystem"/>/<see cref="WeaponSystem"/>/<see cref="ShootingSystem"/>/<see cref="SimRaycast"/>）
    /// 经 <see cref="SimStep"/> 脊柱注入读取，
    /// 不再读任何静态；GM 面板"对局中现场开合"直接写**当前活动局**的实例（写值落在渲染帧之间，
    /// 同进程的客户端预测与本地服同引用同值）。
    ///
    /// 生命周期：进房时由入口（ProcedureMatch——LiteGame 侧，框架不引它）按入口初值新建，
    /// 随会话显式传递（`MatchRulesSeeder` → `MatchTransport` → `AccountSession` → `BattleContext`）供两端绑定
    /// （客户端预测在 StartGame 一次性绑定，之后热路径只读注入实例）；调试投影经 `IDevSession.MatchRules` 读同一实例。
    /// 下次进房整体替换（常规入口初值恒 false——与既有"随入口复位"同语义）。
    /// 入口初值同时进 <see cref="CombatValues"/>（联机身份摘要 + 重放随装载值覆盖）；
    /// 中局开合属开发操作——不进摘要、不可重放（与既有口径一致）。
    /// </summary>
    public sealed class MatchTestRules
    {
        /// <summary>全房免死：跨死亡线时 Hp 保底 1、不写 Kill/Death——命中/受击反馈保留，目标永不消失。</summary>
        public bool NoDeath;

        /// <summary>无限子弹：开火不扣弹匣（也不触发末发自动换弹）、空仓亦可开火（弹匣 0 放行——
        /// 彻底干涸的枪开开关即复活）、换弹请求因满弹被拒——仅测试模式。</summary>
        public bool InfiniteAmmo;

        /// <summary>爆头线下沿试验覆写（m；&lt; 0 = 不覆写，用 <see cref="CombatConfig.HeadHitLine"/> 导出值）：
        /// GM 面板滑杆/爆头带调带工具的**运行态落点**（原 <c>CombatConfig.HeadHitLineDevOverride</c> 静态覆写面的
        /// 同型收口——判定路径不读静态，含 dev 宏内）。与中局开合同裁决：**不进 <see cref="CombatValues"/> 摘要、
        /// 不可重放**（连续调参旋钮进摘要会让每次试验抖动联机身份）；进房初值随入口种子（TestModeRuntime 待用值），
        /// 复位即回导出值。</summary>
        public float HeadHitLineOverride = -1f;

        /// <summary>爆头柱半径试验覆写（m；&lt; 0 = 不覆写）——与 <see cref="HeadHitLineOverride"/> 同口径。</summary>
        public float HeadshotRadiusOverride = -1f;

        /// <summary>武器单发伤害试验覆写（≥ 0 生效，&lt; 0 = 表值）：命中 <see cref="ShootingSystem"/> 的 baseDamage
        /// 解析口消费。与爆头带覆写同裁决——不进 <see cref="CombatValues"/> 摘要、不可重放；进房随 TestModeRuntime
        /// 待用值种子，复位即回表值。</summary>
        public int DamageOverride = -1;

        /// <summary>武器射速试验覆写（rpm，&gt; 0 生效，≤ 0 = 表值）：开火 <see cref="WeaponSystem"/> 的击发节拍
        /// 解析口消费（节拍 = ceil(TickRate×60/rpm) 重算，不改动表定义）。同上不进摘要、不可重放。</summary>
        public int RpmOverride = -1;

        /// <summary>规则位（联机测试房批）：1 = 免死、2 = 无限弹药。随快照下发（`MatchStateDelta.gm_flags`）。</summary>
        public const int FlagNoDeath = 1;
        public const int FlagInfiniteAmmo = 2;

        /// <summary>当前规则的位掩码（0 = 无 GM）。</summary>
        public int GmFlags
        {
            get
            {
                int flags = 0;
                if (NoDeath) flags |= FlagNoDeath;
                if (InfiniteAmmo) flags |= FlagInfiniteAmmo;
                return flags;
            }
        }

        /// <summary>按位掩码构造（联机测试房批：服务端 GM 动作 / 客户端快照应用共用）。</summary>
        public static MatchTestRules FromFlags(int flags)
        {
            return new MatchTestRules
            {
                NoDeath = (flags & FlagNoDeath) != 0,
                InfiniteAmmo = (flags & FlagInfiniteAmmo) != 0,
            };
        }

        /// <summary>本局生效爆头线下沿：覆写优先，否则烘焙导出常量（<paramref name="rules"/> = null ⇒ 恒导出值——release/常规入口形态）。</summary>
        public static float ResolveHeadHitLine(MatchTestRules rules)
            => rules != null && rules.HeadHitLineOverride >= 0f ? rules.HeadHitLineOverride : CombatConfig.HeadHitLine;

        /// <summary>本局生效爆头柱半径：覆写优先，否则烘焙导出常量（<paramref name="rules"/> = null ⇒ 恒导出值）。</summary>
        public static float ResolveHeadshotRadius(MatchTestRules rules)
            => rules != null && rules.HeadshotRadiusOverride >= 0f ? rules.HeadshotRadiusOverride : CombatConfig.HeadshotRadius;

        /// <summary>本局生效武器单发伤害：覆写优先，否则表值（<paramref name="rules"/> = null ⇒ 恒表值——release/常规入口形态）。</summary>
        public static int ResolveWeaponDamage(MatchTestRules rules, int tableDamage)
            => rules != null && rules.DamageOverride >= 0 ? rules.DamageOverride : tableDamage;

        /// <summary>本局生效击发节拍（帧）：rpm 覆写优先（ceil(TickRate×60/rpm) 重算——与 <see cref="WeaponDef"/>
        /// 构造同式），否则表定义节拍（<paramref name="rules"/> = null 或覆写 ≤ 0 ⇒ 恒表值）。</summary>
        public static int ResolveFireIntervalFrames(MatchTestRules rules, int tableRpm, int tableIntervalFrames)
        {
            if (rules == null || rules.RpmOverride <= 0) return tableIntervalFrames;
            return (SimConfig.TickRate * 60 + rules.RpmOverride - 1) / rules.RpmOverride;
        }
    }
}
