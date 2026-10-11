namespace LiteView.Animation
{
    /// <summary>
    /// 换弹持枪幅度的**实时调参面**：烘焙值在 <see cref="GripBake.TorsoWeight"/>（0 = 不约束、
    /// 1 = 完全跟随持枪手），对局内经 GM 面板滑杆实时覆写观感。与
    /// <c>MatchTestRules</c> 爆头带覆写同口径——**仅开发三宏内存在**（release 随宏编译剥离）、
    /// 不落盘、不进任何判定/快照面（纯表现沙箱件）；复位即回烘焙值。
    /// </summary>
    public static class GripTuning
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
        /// <summary>测试模式实时覆写（&lt; 0 = 不覆写，用烘焙值）。</summary>
        public static float TorsoWeightDevOverride = -1f;

        /// <summary>幅度的**实时取值**：测试模式覆写优先，否则烘焙常量。</summary>
        public static float TorsoWeightLive => TorsoWeightDevOverride >= 0f ? TorsoWeightDevOverride : GripBake.TorsoWeight;
#else
        /// <summary>幅度的**实时取值**（release：恒 = 烘焙值——覆写面随三宏编译剥离）。</summary>
        public static float TorsoWeightLive => GripBake.TorsoWeight;
#endif
    }
}
