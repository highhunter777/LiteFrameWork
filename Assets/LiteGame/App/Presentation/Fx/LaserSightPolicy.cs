using System.Collections.Generic;

namespace LiteGame
{
    /// <summary>
    /// 武器瞄准激光的支持策略（客户端表现开关——纯 View，不上双端表/不进 digest；判定与表现见
    /// <see cref="BattleLaserDriver"/>）。按 <see cref="LiteSim.WeaponRuntime.WeaponDefId"/> 集合判定
    /// "哪些武器带激光"——tb_weapon（G2-P1 武器战斗）落地后迁列进表，届时只改本类。
    ///
    /// **开发形态**：<see cref="LiteClient.IDevSession.Active"/> 置位期间由端口开关
    /// <see cref="LiteClient.IDevSession.LaserSight"/> 决定（测试面板/GM 面板可对局中现场开合）；
    /// 非开发形态走 def id 支持位。端口发布实现全关 = 行为与历史 release 一致。
    /// </summary>
    public static class LaserSightPolicy
    {
        /// <summary>支持瞄准激光的武器定义 Id 集合。
        /// 当前生产 WeaponDefId 恒 0（P1 WeaponSystem 未落地——演示步枪走默认选中槽，0 即"演示步枪"），
        /// 故先入集合；真实 def id 随 tb_weapon 落表后在此替换。</summary>
        private static readonly HashSet<int> SupportedDefIds = new HashSet<int> { 0 };

        /// <summary>该武器是否支持瞄准激光（开发形态由端口开关决定——见类注释）。</summary>
        public static bool Supports(int weaponDefId, LiteClient.IDevSession dev = null)
        {
            if (dev != null && dev.Active) return dev.LaserSight;
            return SupportedDefIds.Contains(weaponDefId);
        }
    }
}
