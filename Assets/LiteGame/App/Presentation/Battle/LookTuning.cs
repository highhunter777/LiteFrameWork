namespace LiteGame
{
    /// <summary>
    /// 视角手感的**实时调参面**（<see cref="CrosshairTuning"/> 同族——GM 面板「视角调参」滑杆实时覆写，
    /// 不落盘、不进任何判定/快照面）：覆写优先于机制常量，复位（&lt; 0）即回常量。**仅开发三宏内存在**
    /// （release 随宏编译剥离、恒用常量）；实机定档后把拖定值写回 <see cref="BattleLookController"/> 机制常量
    /// （<c>LookSensitivityRadPerPixel</c>/<c>AdsLookSensitivityScale</c>——单源常量，与准心调参批先例同口径）。
    /// FOV/肩距属**场景 rig 值**（BattleRig.unity Inspector 单源）——GM 页直写运行时 vcam 供试手感，
    /// 不经本类（定档后写回场景）。
    /// </summary>
    public static class LookTuning
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
        /// <summary>视角灵敏度覆写（rad/px；&lt; 0 = 不覆写，用机制常量）。</summary>
        public static float SensitivityDevOverride = -1f;

        /// <summary>ADS 灵敏度收敛比覆写（&lt; 0 = 不覆写）。</summary>
        public static float AdsScaleDevOverride = -1f;

        /// <summary>解析视角灵敏度（覆写优先，否则机制常量）。</summary>
        public static float SensitivityOf(float constValue)
            => SensitivityDevOverride >= 0f ? SensitivityDevOverride : constValue;

        /// <summary>解析 ADS 收敛比（覆写优先，否则机制常量）。</summary>
        public static float AdsScaleOf(float constValue)
            => AdsScaleDevOverride >= 0f ? AdsScaleDevOverride : constValue;
#else
        /// <summary>解析视角灵敏度（release：恒机制常量——覆写面随三宏编译剥离）。</summary>
        public static float SensitivityOf(float constValue) => constValue;

        /// <summary>解析 ADS 收敛比（release：恒机制常量）。</summary>
        public static float AdsScaleOf(float constValue) => constValue;
#endif
    }
}
