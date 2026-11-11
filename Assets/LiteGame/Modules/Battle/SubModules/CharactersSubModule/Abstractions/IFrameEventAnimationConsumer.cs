using LiteFramework.Animation;
using LiteSim;

namespace LiteGame.Modules.Battle.SubModules.CharactersSubModule.Abstractions
{
    /// <summary>
    /// 帧事件 → 角色动画请求的**接缝**（《动画模块专项设计》§8"表现事件"）。
    /// </summary>
    public interface IFrameEventAnimationConsumer
    {
        /// <summary>
        /// 静默门放行后的单个帧事件（**逻辑帧边界**，每逻辑帧一次批量调用；重放/和解段已被门挡掉）。
        /// 实现者按各自状态机的转换条件路由后提交播放；未覆盖的事件类型不处理即可。
        /// </summary>
        void OnFrameEvent(in FrameEvent e);
    }
}
