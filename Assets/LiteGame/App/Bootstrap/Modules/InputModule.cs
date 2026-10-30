using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteFramework;
using LiteBattle;

namespace LiteGame
{
    /// <summary>
    /// ⑩ 输入服务（《角色状态与动作专项设计》§3"输入三件"）：
    /// 设备 → 意图 → 三个门 的协调者。**登记在 Root**（跨对局复用；对局期由 ProcedureBattle
    /// 挂/摘设备源），产品级拦截源（UI 模态）在 <see cref="ContainerModule"/> 的装配点登记一次、
    /// 跨对局常驻——与 UI 服务装配同层，不由流程重复登记。
    ///
    /// **设备源在这里装**（New Input System 适配器）：注入时机是装配根
    /// （组合根本该认识适配器，§5）；对局相机在 <see cref="PresentationModule"/> 建好后经
    /// <see cref="IInputService.SetAimCamera"/> 补上——两者不同时可用，故分两步。
    /// </summary>
    internal sealed class InputModule : IClientModule, IClientModuleGraph
    {
        private InputService _service;
        private NewInputIntentSource _source;

        public string Name => "Input";

        /// <summary>依赖的模块（与 GameEntry 注册序一致；供 Host DAG 校验）。</summary>
        public System.Collections.Generic.IReadOnlyList<System.Type> DependsOn =>
            new System.Type[] { typeof(PresentationModule), typeof(DevSessionModule) };
        public UniTask InitializeAsync(ModuleContext context, CancellationToken ct)
        {
            // 资源归属＝本模块作用域（路线图 §4.3）：初始化中途失败时 Host 释放 scope 即回收，
            // 不必依赖 ShutdownAsync（失败模块不会被调用关闭）。
            context.Scope.Register(new DelegatedDisposable(DisposeModuleResources));
            _service = new InputService();
            LiteBattle.IDevSession dev = context.Require<LiteBattle.IDevSession>();
            _source = new NewInputIntentSource(matchRules: () => dev.MatchRules);   // Action 资产在本类内建；本局规则经读口注入
            _source.SetAimCamera(context.Get<UnityEngine.Camera>());   // ⑩ 表现域建的主相机（注册序保证已就绪；null = 不做换算）
            _service.SetSource(_source);
            context.Put<IInputService>(_service);
            return UniTask.CompletedTask;
        }

        /// <summary>
        /// 关服顺序**不可颠倒**：先 `Dispose` 设备源（它持有 Input System 的 Action 资产——
        /// 生成类 `~PlayerInputActions` 的析构断言要求资产被 GC 时 action map **已 Disable**），
        /// 再清服务状态。
        ///
        /// 这里刻意**不**用 <c>ResetAll()</c>：它内部会清空 <c>Source</c>，而本方法要保证
        /// "设备源一定被 Dispose"——把那条依赖藏在"某个方法顺带做掉"里，改动顺序时就会静默漏掉
        /// （触发点正是生成类析构断言：表现为关服时刷一行 Assert，而非崩溃，很容易被忽略）。
        /// </summary>
        /// <summary>模块资源释放（由模块作用域在关闭/回滚时调用；顺序不可颠倒：
        /// ① 设备源 action map Disable + 资产销毁（必须在清引用之前）→ ② 服务引用/计数全清。</summary>
        private void DisposeModuleResources()
        {
            _source?.Dispose();                          // ① 设备源：action map Disable + 资产销毁（必须在清引用之前）
            _source = null;
            _service?.ResetAll();                        // ② 服务：拦截源/设备源引用/计数全清（关服语义）
            _service = null;
        }

        /// <summary>关闭面：资源已随 <see cref="ModuleContext.Scope"/> 释放（本方法无常驻清理）。</summary>
        public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;
    }
}
