using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteGame.Modules.Input
{
    /// <summary>
    /// ⑭ 输入服务（《角色状态与动作专项设计》§3"输入三件"）：
    /// 设备 → 意图 → 三个门 的协调者。**登记在 Root**（跨对局复用；对局期由 ProcedureBattle
    /// 挂/摘设备源），产品级拦截源（UI 模态）在装配段登记一次、跨对局常驻。
    ///
    /// **设备源由组合根注入**（`NewInputIntentSource` 属 `Adapters.Unity`，该程序集反向引用本域）：
    /// 段只接工厂委托，在 `InitializeAsync` 里用**已就绪的 `IDevSession`** 构造它——
    /// `matchRules` 读口依赖 ③ 段，前置创建会静默丢掉测试房爆头带覆写。
    /// 对局相机在相机段建好后经 `IAimCameraSink` 补上（不同时可用，故分两步）。
    /// </summary>
    public sealed class InputModule : IClientModule, IClientModuleGraph
    {
        private readonly Func<IDevSession, IIntentSource> _createSource;

        private InputService _service;
        private IIntentSource _source;

        public InputModule(Func<IDevSession, IIntentSource> createSource)
            => _createSource = createSource ?? throw new ArgumentNullException(nameof(createSource));

        public string Name => "Input";

        /// <summary>依赖的模块（与 GameEntry 注册序一致；供 Host DAG 校验）。</summary>
        public System.Collections.Generic.IReadOnlyList<string> DependsOn =>
            new[] { "DevSession" };

        public UniTask InitializeAsync(ModuleContext context, CancellationToken ct)
        {
            // 资源归属＝本模块作用域（路线图 §4.3）：初始化中途失败时 Host 释放 scope 即回收，
            // 不必依赖 ShutdownAsync（失败模块不会被调用关闭）。
            context.Scope.Register(new DelegatedDisposable(DisposeModuleResources));
            _service = new InputService();
            IDevSession dev = context.Require<IDevSession>();
            _source = _createSource(dev);                 // 设备源：本局规则经读口注入
            _service.SetSource(_source);
            context.Put<IInputService>(_service);
            return UniTask.CompletedTask;
        }

        /// <summary>模块资源释放（由模块作用域在关闭/回滚时调用；顺序不可颠倒：
        /// ① 设备源 action map Disable + 资产销毁（必须在清引用之前）→ ② 服务引用/计数全清。</summary>
        private void DisposeModuleResources()
        {
            (_source as IDisposable)?.Dispose();          // ① 设备源：action map Disable + 资产销毁（必须在清引用之前）
            _source = null;
            _service?.ResetAll();                        // ② 服务：拦截源/设备源引用/计数全清（关服语义）
            _service = null;
        }

        /// <summary>关闭面：资源已随 <see cref="ModuleContext.Scope"/> 释放（本方法无常驻清理）。</summary>
        public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;
    }
}
