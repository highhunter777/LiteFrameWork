#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using LiteFramework;
using LiteGame.Adapters.Dev.Debug.Kernel;

namespace LiteGame.Adapters.Dev.Debug.Console
{
    /// <summary>处理器：把时钟落回默认（Scale=1、不暂停）——执行成功后按"命令→事实单向流"由执行方发事件（当前无事件需求，注释登记）。</summary>
    public sealed class GMResetClockHandler : ICommandHandler<GMResetClockCommand>
    {
        private readonly DebugRuntimeTuner _tuner;
        public GMResetClockHandler(DebugRuntimeTuner tuner) { _tuner = tuner; }

        public CommandResult Execute(GMResetClockCommand command)
        {
            _tuner.ResetClock();
            return CommandResult.Success("时钟已重置（Scale=1、不暂停）");
        }
    }
}
#endif
