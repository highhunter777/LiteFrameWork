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
    /// **设备源在这里装**（New Input System 适配器，2026-09-26 接入）：注入时机是装配根
    /// （组合根本该认识适配器，§5）；对局相机在 <see cref="PresentationModule"/> 建好后经
    /// <see cref="IInputService.SetAimCamera"/> 补上——两者不同时可用，故分两步。
    /// </summary>
    internal sealed class InputModule : IClientModule
    {
        private InputService _service;
        private NewInputIntentSource _source;

        public string Name => "Input";

        public UniTask InitializeAsync(ClientContext context, CancellationToken ct)
        {
            _service = new InputService();
            _source = new NewInputIntentSource();          // Action 资产在本类内建
            _source.SetAimCamera(context.Get<UnityEngine.Camera>());   // ⑨ 表现壳建的主相机（注册序保证已就绪；null = 不做换算）
            _service.SetSource(_source);
            context.Put<IInputService>(_service);
            return UniTask.CompletedTask;
        }

        public UniTask ShutdownAsync(CancellationToken ct)
        {
            _service?.ResetAll();                        // 关服清干净（拦截源/设备源/计数；静态清零语义的实例版）
            _source?.Dispose();                          // 设备源的资产/订阅释放（action map Disable + 资产销毁）
            _source = null;
            return UniTask.CompletedTask;
        }
    }
}
