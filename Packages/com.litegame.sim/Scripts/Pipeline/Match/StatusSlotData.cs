namespace LiteSim
{
    /// <summary>
    /// 单状态效果槽（§3.1 固定布局 StatusSlot[entity, 4]）。
    /// </summary>
    public partial struct StatusSlotData
    {
        /// <summary>效果定义 Id（0 = 空槽）。**私有面**：他人只见 <see cref="EntitySlot.Shield"/> 投影。</summary>
        [StateLayer(StateLayer.Private)]
        public int EffectId;

        /// <summary>效果结束帧。**私有面**（同上）。</summary>
        [StateLayer(StateLayer.Private)]
        public int EndFrame;

        /// <summary>效果参数（护盾=剩余护盾值；按效果释义）。**私有面**（同上）。</summary>
        [StateLayer(StateLayer.Private)]
        public int Param;
    }
}
