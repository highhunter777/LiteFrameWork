namespace LiteGame
{
    /// <summary>
    /// 视角手感的**实时调参面**（CrosshairTuning 同族——GM 面板「视角调参」滑杆实时覆写， 不落盘、不进任何判定/快照面）：覆写优先于机制常量，复位（&lt）。
    /// </summary>
    public static class LookTuning
    {
        /// <summary>视角灵敏度覆写（rad/px；&lt; 0 = 不覆写，用机制常量；发布形态无写入者）。</summary>
        public static float SensitivityDevOverride = -1f;

        /// <summary>ADS 灵敏度收敛比覆写（&lt; 0 = 不覆写）。</summary>
        public static float AdsScaleDevOverride = -1f;

        /// <summary>解析视角灵敏度（覆写优先，否则机制常量）。</summary>
        public static float SensitivityOf(float constValue)
            => SensitivityDevOverride >= 0f ? SensitivityDevOverride : constValue;

        /// <summary>解析 ADS 收敛比（覆写优先，否则机制常量）。</summary>
        public static float AdsScaleOf(float constValue)
            => AdsScaleDevOverride >= 0f ? AdsScaleDevOverride : constValue;
    }
}
