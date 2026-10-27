using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteClient;

namespace LiteGame
{
    /// <summary>⑥ Lua 宿主（同 GameObject 组件）：Init/DoMain 在 Preload 锚点；退出 Shutdown 释放 env/回调。</summary>
    internal sealed class LuaHostModule : IClientModule, IClientModuleGraph
    {
        private readonly UnityEngine.GameObject _hostObject;
        private LuaComponent _lua;

        public string Name => "LuaHost";

        /// <summary>依赖的模块（与 GameEntry 注册序一致；供 Host DAG 校验）。</summary>
        public System.Collections.Generic.IReadOnlyList<System.Type> DependsOn => System.Array.Empty<System.Type>();
        public LuaHostModule(UnityEngine.GameObject hostObject) => _hostObject = hostObject;

        public UniTask InitializeAsync(ModuleContext context, CancellationToken ct)
        {
            _lua = _hostObject.AddComponent<LuaComponent>();
            context.Put(_lua);
            return UniTask.CompletedTask;
        }

        public UniTask ShutdownAsync(CancellationToken ct)
        {
            _lua?.Shutdown();
            return UniTask.CompletedTask;
        }
    }
}
