namespace LiteGame.Modules.Camera
{
    /// <summary>
    /// 瞄准机沿主视线前移距离曲线（纯二次）：D(d) = d² / base（d &lt）。
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