using System.Collections.Generic;

namespace LiteGame
{
    /// <summary>
    /// 武器瞄准激光的支持策略（客户端表现开关——纯 View，不上双端表/不进 digest；判定与表现见
    /// <see cref="BattleLaserDriver"/>）。按 <see cref="LiteSim.WeaponRuntime.WeaponDefId"/> 集合判定
    /// "哪些武器带激光"——tb_weapon（G2-P1 武器战斗）落地后迁列进表，届时只改本类。
    ///
    /// **测试模式直通**：<see cref="TestModeRuntime.Active"/> 置位期间任何武器都支持
    /// （测试面板/F10 进测试房即生效；TestModeRuntime 整段剥离于 release——见同款 #if 守卫）。
    /// </summary>
    public static class LaserSightPolicy
    {
        /// <summary>支持瞄准激光的武器定义 Id 集合。
        /// 当前生产 WeaponDefId 恒 0（P1 WeaponSystem 未落地——演示步枪走默认选中槽，0 即"演示步枪"），
        /// 故先入集合；真实 def id 随 tb_weapon 落表后在此替换。</summary>
        private static readonly HashSet<int> SupportedDefIds = new HashSet<int> { 0 };

        /// <summary>该武器是否支持瞄准激光（测试模式下任何武器都支持——见类注释）。</summary>
        public static bool Supports(int weaponDefId)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
            if (TestModeRuntime.Active) return true;
#endif
            return SupportedDefIds.Contains(weaponDefId);
        }
    }
}
