namespace LiteGame
{
    /// <summary>
    /// 瞄准机沿主视线前移距离曲线（纯二次）：<c>D(d) = d² / base</c>（d &lt; base），d ≥ base 不干预（回 base）。
    /// 性质：过原点、在 d=base 处连续接回场景主/瞄准基座差、[0, base) 内恒有 D &lt; d
    /// （瞄准点越近，ADS 相机越贴近主相机——近距贴脸时不会停在瞄准点后方）。
    /// </summary>
    public static class AimOffsetCurve
    {
        /// <param name="distance">瞄准点在视角前向上的投影距离（米；负值由调用方夹住）。</param>
        /// <param name="zBase">场景主/瞄准基座差（米；≤0 = 配置无效，回 0）。</param>
        public static float Z(float distance, float zBase)
        {
            if (zBase <= 0f || distance <= 0f) return 0f;
            if (distance >= zBase) return zBase;
            return distance * distance / zBase;
        }
    }
}