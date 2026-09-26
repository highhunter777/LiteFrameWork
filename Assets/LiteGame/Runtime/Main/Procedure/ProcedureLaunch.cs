using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteSim.View;

namespace LiteGame
{
    /// <summary>
    /// 启动流程：**移交**（原"唯一受信装配点"——业务服务注册已按 §5.1 逻辑边界**全量归还装配根**
    /// <see cref="GameModules"/>：注册是装配职责，且 UI 服务在 Runtime 侧注册会让流程层必须认识 UI 运行时，
    /// 与 `Runtime → UI` 成环、拆不出 asmdef）。
    ///
    /// 本阶段现在只剩一件事：请求进入内容事务。**Start 由 GameEntry.Start() 触发**
    /// （晚于全部组件 Awake 的 RegisterInstance——流程顺序契约，避免密封后注册违例）。
    /// </summary>
    public sealed class ProcedureLaunch : ProcedureStageBase<ProcedureId, ProcedureArgs>
    {
        public ProcedureLaunch(CancellationToken rootToken = default)
            : base(rootToken)
        {
        }

        protected override void RunAsync(IStageHost<ProcedureId, ProcedureArgs> m, in ProcedureArgs req, CancellationToken ct)
            => RunAsyncCore(m, ct).Forget();          // 一行转发，仅此而已——禁止 async void（M0 指导 §6）

        private async UniTask RunAsyncCore(IStageHost<ProcedureId, ProcedureArgs> m, CancellationToken ct)
        {
            try
            {
                m.Request(ProcedureId.Patch);            // C1-⑩：先过内容事务（启动恢复 + 资源包初始化）再预载
            }
            catch (OperationCanceledException) { /* 正常取消，静默 */ }
            catch (Exception ex)
            {
                Fail(m, ex, nameof(RunAsyncCore));
                m.Request(ProcedureId.Error, new ProcedureArgs(ex));
            }
        }
    }
}
