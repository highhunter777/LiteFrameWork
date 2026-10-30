using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteFramework;

namespace LiteGame
{
    /// <summary>⑥ Lua 宿主（同 GameObject 组件）：Init/DoMain 在 Preload 锚点；退出 Shutdown 释放 env/回调。</summary>
    public sealed class LuaHostModule : IClientModule, IClientModuleGraph
    {
        private readonly UnityEngine.GameObject _hostObject;
        private LuaComponent _lua;

        public string Name => "LuaHost";

        /// <summary>依赖的模块（与 GameEntry 注册序一致；供 Host DAG 校验）。</summary>
        public System.Collections.Generic.IReadOnlyList<string> DependsOn => System.Array.Empty<string>();
        public LuaHostModule(UnityEngine.GameObject hostObject) => _hostObject = hostObject;

        public UniTask InitializeAsync(ModuleContext context, CancellationToken ct)
        {
            // 资源归属＝本模块作用域（路线图 §4.3）：Lua env/回调随模块关闭或初始化失败释放。
            context.Scope.Register(new DelegatedDisposable(DisposeModuleResources));
            _lua = _hostObject.AddComponent<LuaComponent>();
            context.Put(_lua);
            return UniTask.CompletedTask;
        }

        private void DisposeModuleResources()
        {
            _lua?.Shutdown();
            _lua = null;
        }

        /// <summary>关闭面：Lua 宿主资源已随 <see cref="ModuleContext.Scope"/> 释放。</summary>
        public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;
    }
}
