namespace LiteFramework.Animation
{
    /// <summary>
    /// 混合定义：语义 ID → 有序的绑定槽位集合（槽位序 = 请求权重次序）。
    ///
    /// **集合形态固定**——权重只调比例，不增删槽位（换集合 = 换 ID/换定义）；因此槽位数能在解析期
    /// 与后端容量对齐校验（每角色状态/节点/过渡尾部有上限）。
    /// **槽位应当是循环片段**：混合集合恒为循环形态（无单一结束边界），一次性片段在混合里会停在末帧
    /// （不逐输入强制回绕——典型用途是循环片段之间按速度连续混合）。
    /// </summary>
    public readonly struct AnimationBlendDefinition
    {
        public readonly AnimationId Id;
        public readonly AnimationChannel Channel;
        /// <summary>槽位绑定（槽位序；构造时克隆，视为只读）。</summary>
        public readonly string[] Bindings;
        /// <summary>合法速度区间（含端点）；越界请求按 <see cref="FallbackPolicy.Reject"/> 拒绝。</summary>
        public readonly float MinSpeed;
        public readonly float MaxSpeed;

        public AnimationBlendDefinition(AnimationId id, AnimationChannel channel, string[] bindings,
            float minSpeed = 0.01f, float maxSpeed = 4f)
        {
            Id = id;
            Channel = channel;
            Bindings = bindings == null ? null : (string[])bindings.Clone();
            MinSpeed = minSpeed;
            MaxSpeed = maxSpeed;
        }

        public int SlotCount => Bindings?.Length ?? 0;

        public bool IsValid => Id.IsValid && SlotCount > 0;
    }
}