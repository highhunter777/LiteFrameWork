namespace LiteSim
{
    /// <summary>
    /// 区域效果分型行：<c>Zones[slot]</c>——行有效 ⟺ KindZone 置位。
    /// EMP/雷达等驻留区域：出生点 + 半径 + 剩余时长（区域系统衰减——尚未实现，G2 排期）。
    /// </summary>
    public partial struct ZoneState
    {
        /// <summary>tbitemconfig 行 id（半径/持续时长的来源行）。</summary>
        [StateLayer(StateLayer.Public)]
        public int ItemDefId;

        /// <summary>区域半径（m，出生时刻按表展开）。</summary>
        [StateLayer(StateLayer.Public)]
        public float Radius;

        /// <summary>剩余持续帧数（每帧递减，0 即回收——区域系统尚未实现，G2 排期）。</summary>
        [StateLayer(StateLayer.Public)]
        public int RemainingFrames;

        /// <summary>归属实体 Id（雷达私有可见性/EMP 中立判定，由区域系统维护——尚未实现，G2 排期）。</summary>
        [StateLayer(StateLayer.Public)]
        public long OwnerId;
    }
}
