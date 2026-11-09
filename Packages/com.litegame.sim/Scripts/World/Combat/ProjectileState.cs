namespace LiteSim
{
    /// <summary>
    /// 投掷物分型行：<c>Projectiles[slot]</c>——行有效 ⟺ KindProjectile 置位。
    /// 手雷/闪光等抛物面（hitscan 之外的弹道命中由投掷物分型系统结算——尚未实现，G2 排期）。
    /// </summary>
    public partial struct ProjectileState
    {
        /// <summary>tbitemconfig 行 id（伤害/半径/衰减/投掷物速度的来源行）。</summary>
        [StateLayer(StateLayer.Public)]
        public int ItemDefId;

        /// <summary>弹道速度（发射时刻按表投影掷物速度；分型行持有展开值，重放不依赖表）。</summary>
        [StateLayer(StateLayer.Public)]
        public float Speed;

        /// <summary>引信到期帧（0 = 碰撞即炸；>0 = 定时雷）。</summary>
        [StateLayer(StateLayer.Public)]
        public int DetonateFrame;

        /// <summary>发射者实体 Id（伤害归属与击杀账目用）。</summary>
        [StateLayer(StateLayer.Public)]
        public long OwnerId;
    }
}
