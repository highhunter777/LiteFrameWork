using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteGame
{
    /// <summary>
    /// ⑩ 输入服务（2026-09-26 输入服务批，《角色状态与动作专项设计》§3"输入三件"）：
    /// 设备 → 意图 → 三个门 的协调者。**登记在 Root**（跨对局复用；对局期由 ProcedureBattle
    /// 挂/摘设备源），产品级拦截源（UI 模态）在 <see cref="ContainerModule"/> 的装配点登记一次、
    /// 跨对局常驻——与 UI 壳同层，不由流程重复登记。
    ///
    /// 放在 UiShell **之后**（依赖序）与 ⑪ContainerModule **之前**：后者的流程装配需要它，
    /// 而它的 UI 依赖经委托（<c>uiService.IsModalOpen</c>）注入，不认识 UI 运行时（§5.1 逻辑边界）。
    /// </summary>
    internal sealed class InputModule : IClientModule
    {
        private InputService _service;

        public string Name => "Input";

        public UniTask InitializeAsync(ClientContext context, CancellationToken ct)
        {
            _service = new InputService();
            context.Put<IInputService>(_service);
            return UniTask.CompletedTask;
        }

        public UniTask ShutdownAsync(CancellationToken ct)
        {
            _service?.ResetAll();                        // 关服清干净（拦截源/设备源/计数；静态清零语义的实例版）
            return UniTask.CompletedTask;
        }
    }
}
