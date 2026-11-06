namespace LiteSim
{
    /// <summary>
    /// 每局测试规则持有件（**可变引用**）：免死/无限子弹的**运行态单源**——确定性系统 （<see cref="DamageSystem"/>/<see cref="WeaponSystem"/>/<see cref=。）。
    /// </summary>
    public sealed class MatchTestRules
    {
        /// <summary>全房免死：跨死亡线时 Hp 保底 1、不写 Kill/Death——命中/受击反馈保留，目标永不消失。</summary>
        public bool NoDeath;

        /// <summary>无限子弹：开火不扣弹匣（也不触发末发自动换弹）、空仓亦可开火（弹匣 0 放行——
        /// 彻底干涸的枪开开关即复活）、换弹请求因满弹被拒——仅测试模式。</summary>
        public bool InfiniteAmmo;

        /// <summary>武器单发伤害试验覆写（≥ 0 生效，&lt; 0 = 表值）：命中 <see cref="ShootingSystem"/> 的 baseDamage
        /// 解析口消费。不进 <see cref="CombatValues"/> 摘要、不可重放；进房随 TestModeRuntime
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
