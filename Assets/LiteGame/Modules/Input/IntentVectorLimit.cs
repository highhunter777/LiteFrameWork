namespace LiteGame.Modules.Input
{
    /// <summary>
    /// 意图向量的闸门边界守卫（《角色状态与动作专项设计》§3"长度 ≤1 契约"的浮点边界收口）。
    /// </summary>
    public static class IntentVectorLimit
    {
        /// <summary>越界收缩因子（1-1e-6：收缩后长度平方 ≈ 1-2e-6，裕量远大于舍入误差 ~1e-7）。</summary>
        private const float ShrinkFactor = 0.999999f;

        /// <summary>
        /// 确保二维向量过闸（长度平方 ≤ 1）。
        /// </summary>
        public static void EnsureWithinLengthLimit(ref float x, ref float z)
        {
            if (x * x + z * z <= 1f) return;
            x *= ShrinkFactor;
            z *= ShrinkFactor;
        }
    }
}
