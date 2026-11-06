using System;

namespace LiteFramework.Animation
{
    /// <summary>
    /// Profile 登记行：**配置面（数据表/资产）到 <see cref="AnimationProfile"/> 的唯一输送形态**—— 零引擎、零表框架依赖的纯数据载体（表适配器把生成行翻译成本行。
    /// </summary>
    public sealed class AnimationProfileRow
    {
        /// <summary>行身份＝表主键：Single/Blend 为语义 ID 串；Fallback 为未登记的源语义 ID；
        /// MaskExclusion 为排除子树路径。整张行集内唯一（定义间"同 ID 不得两栖"互斥、回退源无定义不撞、
        /// 路径行身份独占）。</summary>
        public string Id = "";

        /// <summary>模型族（装载时按族过滤——一表多族的分派键；行归本族才登记）。</summary>
        public string ModelFamily = "";

        public AnimationProfileRowKind Kind;

        /// <summary>通道（Single/Blend 用）。</summary>
        public AnimationChannel Channel;

        /// <summary>Single：后端绑定键（与片源清单键同规）。</summary>
        public string Binding = "";

        /// <summary>Blend：槽位绑定集（槽位序＝权重数组序）。</summary>
        public string[] Bindings = Array.Empty<string>();

        /// <summary>Single：循环定义（循环播放不产生自然 Completed）。</summary>
        public bool Loop;

        /// <summary>Single/Blend：合法速度区间（含端点；越界请求拒绝）。</summary>
        public float MinSpeed;

        /// <summary>Single/Blend：合法速度区间上界。</summary>
        public float MaxSpeed;

        /// <summary>Single：自然完成后持末帧（帧锁定——通道不停机）。</summary>
        public bool HoldOnFinish;

        /// <summary>Single：是否需要资源装载（false＝预加载命中即提交）。</summary>
        public bool RequiresLoad;

        /// <summary>Fallback：回退目标 ID（须已登记——装载器两遍扫保证行序无关；其余类别不用）。</summary>
        public string FallbackId = "";
    }
}
