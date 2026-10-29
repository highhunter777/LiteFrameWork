namespace LiteView.Animation
{
    /// <summary>
    /// 单通道诊断读数（《动画模块专项设计》§11；纯值结构，调用方复用，零分配）。
    /// <see cref="Source"/> 为片段名 / "controller"（基础层开局默认姿态）/ "none"。
    /// <see cref="Weight"/> 是**混合器上该层当前的实际输入权重**（引擎真值，非本类记账值）——
    /// 基础层读回 0 即"通道在播、姿态不落地"。
    ///
    /// 与后端实现分文件：本结构是**观测面契约**（DevHUD/测试消费），后端的图拓扑与混合状态机
    /// 变化时它不该跟着动。
    /// </summary>
    public readonly struct AnimationChannelDebug
    {
        public readonly bool Active;
        public readonly string Binding;
        /// <summary>混合器实际输入权重（基础层健康态恒为 1）。</summary>
        public readonly float Weight;
        public readonly float TailWeight;
        public readonly float Time;
        public readonly float Length;
        public readonly bool Loop;
        /// <summary>引擎真值：混合器/片段节点上实际生效的整体倍率（TrySetBlendSpeed 就地更新当场可见）。
        /// 混合节点报**混合器节点自身**倍率——逐输入净速率路径（TrySetBlendInputSpeeds）下它归 1，
        /// 各输入实际值见 <see cref="InputSpeed0"/>…。</summary>
        public readonly float Speed;

        /// <summary>混合节点逐输入倍率的有效项数（单片段节点 = 0；容量上限 = Profile.MaxBlendSlots）。
        /// 与权重同纪律：报**引擎真值**——只记调用方参数的诊断会把"没生效"伪装成全绿。</summary>
        public readonly int InputSpeedCount;
        public readonly float InputSpeed0;
        public readonly float InputSpeed1;
        public readonly float InputSpeed2;
        public readonly float InputSpeed3;

        public readonly string Source;

        public AnimationChannelDebug(bool active, string binding, float weight, float tailWeight,
            float time, float length, bool loop, float speed, string source,
            int inputSpeedCount = 0, float inputSpeed0 = 0f, float inputSpeed1 = 0f,
            float inputSpeed2 = 0f, float inputSpeed3 = 0f)
        {
            Active = active;
            Binding = binding;
            Weight = weight;
            TailWeight = tailWeight;
            Time = time;
            Length = length;
            Loop = loop;
            Speed = speed;
            Source = source;
            InputSpeedCount = inputSpeedCount;
            InputSpeed0 = inputSpeed0;
            InputSpeed1 = inputSpeed1;
            InputSpeed2 = inputSpeed2;
            InputSpeed3 = inputSpeed3;
        }
    }
}