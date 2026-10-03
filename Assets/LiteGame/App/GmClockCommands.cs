#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using LiteFramework;

namespace LiteGame
{
    /// <summary>GM 示范命令（《命令中心专项设计》§6/批2）：重置时钟——离散调试动作命令化的首个闭环样例；
    /// 与 DebugTuner 连续旋钮并存不互斥（旋钮不强行命令化）。</summary>
    public sealed class GmResetClockCommand : ICommand { }

    /// <summary>处理器：把时钟落回默认（Scale=1、不暂停）——执行成功后按"命令→事实单向流"由执行方发事件（当前无事件需求，注释登记）。</summary>
    public sealed class GmResetClockHandler : ICommandHandler<GmResetClockCommand>
    {
        private readonly DebugTuner _tuner;
        public GmResetClockHandler(DebugTuner tuner) { _tuner = tuner; }

        public CommandResult Execute(GmResetClockCommand command)
        {
            _tuner.WorldTimeScale = 1f;
            _tuner.WorldPaused = false;
            _tuner.UiPaused = false;
            return CommandResult.Success("时钟已重置（Scale=1、不暂停）");
        }
    }
}
#endif
