using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteGame
{
    /// <summary>
    /// 主流程占位（M2）。M5 纵向切片起在这里接主菜单 → 进场景 → 战斗 → 结算的流程树。
    /// 后续阶段（Match/Battle/Result）见 `ProcedureId` 的预留说明与《UI扩展能力设计》§9.1。
    /// </summary>
    public sealed class ProcedureMain : ProcedureStageBase<ProcedureId, ProcedureArgs>
    {
        public ProcedureMain(CancellationToken rootToken = default) : base(rootToken) { }

        protected override void RunAsync(IStageHost<ProcedureId, ProcedureArgs> m, in ProcedureArgs req, CancellationToken ct)
        {
            // 空转待命——无 Request 请求即停在本阶段
        }
    }
}
