using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteGame
{
    /// <summary>⑥ Lua 宿主（同 GameObject 组件）：Init/DoMain 在 Preload 锚点；退出 Shutdown 释放 env/回调。</summary>
    internal sealed class LuaHostModule : IClientModule
    {
        private readonly UnityEngine.GameObject _hostObject;
        private LuaComponent _lua;

        public string Name => "LuaHost";

        public LuaHostModule(UnityEngine.GameObject hostObject) => _hostObject = hostObject;

        public UniTask InitializeAsync(ClientContext context, CancellationToken ct)
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
