#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using System.Collections.Generic;
using LiteClient;
using LiteSim;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// 武器试验参数（开发三宏内）：单发伤害/射速的运行态覆写——判定面数值，覆写走 <see cref="MatchTestRules"/>
    /// 实例字段（R13：机制不读静态），默认值来自武器表（未装载回落内置默认步枪，同值同源由 L1 守卫钉住）。
    /// 当前对局规则经 <see cref="IDevSession.MatchRules"/> 读（实例状态，无 Sim 侧静态"当前局"）。
    /// 覆写只对本地测试模式生效（随进房种子/活动局直写），不落盘、不进摘要、release 剥离。
    /// </summary>
    public sealed class WeaponDebugTunables : IDebugTunableProvider
    {
        private readonly IDevSession _dev;

        public WeaponDebugTunables(IDevSession dev = null) => _dev = dev;

        public void CollectDebugTunables(ICollection<DebugTunable> into)
        {
            WeaponDef d = WeaponConfig.Default;
            into.Add(new DebugTunable
            {
                Id = "weapon.damage",
                Group = "武器试验",
                Label = "单发伤害（默认步枪）",
                Kind = DebugTunableKind.Float,
                Scope = DebugTunableScope.Sandbox,
                Default = d.Damage,
                Min = 0f,
                Max = 200f,
                Decimals = 0,
                RuntimeVisible = true,
                Get = CurrentDamage,
                Set = value => SetDamage(Mathf.RoundToInt(value)),
                Reset = () => SetDamage(-1),
            });
            into.Add(new DebugTunable
            {
                Id = "weapon.rpm",
                Group = "武器试验",
                Label = "射速 rpm（默认步枪）",
                Kind = DebugTunableKind.Float,
                Scope = DebugTunableScope.Sandbox,
                Default = d.Rpm,
                Min = 60f,
                Max = 1200f,
                Decimals = 0,
                RuntimeVisible = true,
                Get = CurrentRpm,
                Set = value => SetRpm(Mathf.RoundToInt(value)),
                Reset = () => SetRpm(-1),
            });
        }

        // ---- 覆写落点（活动局 + 跨局待用值双落，与爆头带覆写同口径）----

        public float CurrentDamage()
        {
            var match = _dev?.MatchRules;
            int table = WeaponConfig.Default.Damage;
            if (match != null) return MatchTestRules.ResolveWeaponDamage(match, table);
            return TestModeRuntime.Current.WeaponDamageOverride >= 0 ? TestModeRuntime.Current.WeaponDamageOverride : table;
        }

        public float CurrentRpm()
        {
            var match = _dev?.MatchRules;
            int table = WeaponConfig.Default.Rpm;
            if (match != null) return match.RpmOverride > 0 ? match.RpmOverride : table;
            return TestModeRuntime.Current.WeaponRpmOverride > 0 ? TestModeRuntime.Current.WeaponRpmOverride : table;
        }

        public void SetDamage(int value)
        {
            TestModeRuntime.Current.WeaponDamageOverride = value;
            var match = _dev?.MatchRules;
            if (match != null) match.DamageOverride = value;
        }

        public void SetRpm(int value)
        {
            TestModeRuntime.Current.WeaponRpmOverride = value;
            var match = _dev?.MatchRules;
            if (match != null) match.RpmOverride = value;
        }
    }
}
#endif
