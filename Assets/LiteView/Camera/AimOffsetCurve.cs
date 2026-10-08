namespace LiteSim.View
{
    /// <summary>
    /// 瞄准机构图 z 偏移曲线（纯二次）：<c>Z(d) = d² / zBase</c>（d &lt; zBase），d ≥ zBase 不干预（回 zBase）。
    /// 性质：过原点、在 d=zBase 处连续接回场景值、[0, zBase) 内恒有 Z &lt; d（瞄准点领先于相机目标点）。
    /// </summary>
    public static class AimOffsetCurve
    {
        /// <param name="distance">瞄准点在焦点前向上的投影距离（米；负值由调用方夹住）。</param>
        /// <param name="zBase">场景配置的构图 z 偏移基准（米；≤0 = 配置无效，回 0）。</param>
        public static float Z(float distance, float zBase)
        {
            if (zBase <= 0f || distance <= 0f) return 0f;
            if (distance >= zBase) return zBase;
            return distance * distance / zBase;
        }
    }
}