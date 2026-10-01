namespace LiteSim.View.Animation
{
    /// <summary>
    /// 单通道诊断读数（《动画模块专项设计》§11；纯值结构，调用方复用，零分配）。
    /// <see cref="Source"/> 为片段名 / "controller"（基础层开局默认姿态）/ "none"。
    /// <see cref="Weight"/> 是**混合器上该层当前的实际输入权重**（引擎真值，非本类记账值）——
    /// 基础层读回 0 即"通道在播、姿态不落地"（2026-09-27 实测缺陷的观测点）。
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
        /// <summary>引擎真值：混合器/片段节点上实际生效的速度（TrySetBlendSpeed 就地更新当场可见）。</summary>
        public readonly float Speed;
        public readonly string Source;

        public AnimationChannelDebug(bool active, string binding, float weight, float tailWeight,
            float time, float length, bool loop, float speed, string source)
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
        }
    }
}