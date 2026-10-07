namespace LiteSim
{
    /// <summary>
    /// 地面道具分型行（《Sim组织专项设计》§2）：<c>Items[slot]</c>——行有效 ⟺
    /// 槽位活体且 <see cref="EntityFlags.KindItem"/> 置位（kind 位 = "该槽位持有哪张分型表数据"的
    /// 迷你 archetype mask）。携带面不在本表——进背包后是 <c>MatchBag</c> 的事（既有面）。
    ///
    /// 布局契约同 P0 运行态：纯值类型 struct、定容数组行、零引擎依赖；进快照/checksum 的字段
    /// 只允许值类型（<c>SimWorldState</c> 布局硬约束 #5 同款）。消费者 = ItemSystem。
    /// </summary>
    public struct ItemState
    {
        /// <summary>tbitemconfig 行 id（道具类型语义：1医疗包 2护盾电池 3手雷 4EMP 5雷达 6钩爪 7传送）。</summary>
        public int ItemDefId;

        /// <summary>堆叠数量（同格同型叠加；拾取时并入 MatchBag，本行随之清零）。</summary>
        public int Count;

        /// <summary>归属实体 Id（0 = 无主地面件；被拾取判定的近侧候选标记，ItemSystem 维护）。</summary>
        public long OwnerId;

        /// <summary>已存在时长（逻辑帧计——刷新节拍与"超时未拾取回收"用）。</summary>
        public int AgeFrames;
    }

    /// <summary>
    /// 投掷物分型行：<c>Projectiles[slot]</c>——行有效 ⟺ KindProjectile 置位。
    /// 手雷/闪光等抛物面（hitscan 之外的弹道命中在 ProjectileSystem 结算）。
    /// </summary>
    public struct ProjectileState
    {
        /// <summary>tbitemconfig 行 id（伤害/半径/衰减/投掷物初速的来源行）。</summary>
        public int ItemDefId;

        /// <summary>弹道速度（发射时刻按表投影掷物速度；分型行持有展开值，重放不依赖表）。</summary>
        public float Speed;

        /// <summary>引信到期帧（0 = 碰撞即炸；>0 = 定时雷）。</summary>
        public int DetonateFrame;

        /// <summary>发射者实体 Id（伤害归属与击杀账目用）。</summary>
        public long OwnerId;
    }

    /// <summary>
    /// 区域效果分型行：<c>Zones[slot]</c>——行有效 ⟺ KindZone 置位。
    /// EMP/雷达等驻留区域：出生点 + 半径 + 剩余时长（ZoneSystem 衰减）。
    /// </summary>
    public struct ZoneState
    {
        /// <summary>tbitemconfig 行 id（半径/持续时长的来源行）。</summary>
        public int ItemDefId;

        /// <summary>区域半径（m，出生时刻按表展开）。</summary>
        public float Radius;

        /// <summary>剩余持续帧数（每帧递减，0 即由 ZoneSystem 回收）。</summary>
        public int RemainingFrames;

        /// <summary>归属实体 Id（雷达私有可见性/EMP 中立判定，ZoneSystem 维护）。</summary>
        public long OwnerId;
    }
}
