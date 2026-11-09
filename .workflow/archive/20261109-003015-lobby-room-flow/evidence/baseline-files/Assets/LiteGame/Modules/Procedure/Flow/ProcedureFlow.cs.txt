using LiteFramework;

namespace LiteGame
{
    /// <summary>流程请求端口的流程段实现（<see cref="IAppFlow"/>）：把语义动作翻成阶段机请求。
    /// 只有这里认识 <see cref="ProcedureId"/>/<see cref="ProcedureArgs"/>——联机入口只认端口。</summary>
    internal sealed class ProcedureFlow : IAppFlow
    {
        private readonly IStageHost<ProcedureId, ProcedureArgs> _machine;

        internal ProcedureFlow(IStageHost<ProcedureId, ProcedureArgs> machine)
            => _machine = machine ?? throw new System.ArgumentNullException(nameof(machine));

        public bool IsAtMain => _machine.Current == ProcedureId.Main;

        public void RequestMatch(MetaJoin join)
            => _machine.Request(ProcedureId.Match, new ProcedureArgs(metaJoin: join));

        public void RequestMain() => _machine.Request(ProcedureId.Main);
    }
}
