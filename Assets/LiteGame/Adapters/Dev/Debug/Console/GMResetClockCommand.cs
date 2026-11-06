#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using LiteFramework;

namespace LiteGame
{
    /// <summary>GM 示范命令（《命令中心专项设计》§6/批2）：重置时钟——离散调试动作命令化的首个闭环样例；
    /// 与 <see cref="DebugRuntimeTuner"/> 连续旋钮并存不互斥（旋钮不强行命令化）。</summary>
    public sealed class GMResetClockCommand : ICommand { }
}
#endif
