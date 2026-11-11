using System.Collections.Generic;
using LiteFramework;
using LiteGame;

namespace LiteGame.Modules.Battle.SubModules.CombatSubModule.Weapon
{
    /// <summary>
    /// 武器瞄准激光的支持策略（客户端表现开关——纯 View，不上双端表/不进 digest）。
    /// </summary>
    public static class LaserSightPolicy
    {
        /// <summary>支持瞄准激光的武器定义 Id 集合。
        /// 当前生产 WeaponDefId 恒 0（P1 WeaponSystem 未落地——演示步枪走默认选中槽，0 即"演示步枪"），
        /// 故先入集合；真实 def id 随 tb_weapon 落表后在此替换。</summary>
        private static readonly HashSet<int> SupportedDefIds = new HashSet<int> { 0 };

        /// <summary>该武器是否支持瞄准激光（开发形态由端口开关决定——见类注释）。</summary>
        public static bool Supports(int weaponDefId, LiteGame.IDevSession dev = null)
        {
            if (dev != null && dev.Active) return dev.LaserSight;
            return SupportedDefIds.Contains(weaponDefId);
        }
    }
}
