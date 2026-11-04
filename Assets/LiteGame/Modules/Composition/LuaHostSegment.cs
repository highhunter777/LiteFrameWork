using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteFramework;

namespace LiteGame.Modules.Composition
{
    /// <summary>⑥ Lua 宿主（同 GameObject 组件）：Init/DoMain 在 Preload 锚点；退出 Shutdown 释放 env/回调。</summary>
    public sealed class LuaHostSegment : IClientSegment, IClientSegmentGraph
    {
        private readonly UnityEngine.GameObject _hostObject;
        private readonly System.Func<UnityEngine.GameObject, ILuaHost> _createHost;
        private ILuaHost _lua;

        public string Name => "LuaHost";

        /// <summary>依赖的模块（与 GameEntry 注册序一致；供 Host DAG 校验）。</summary>
        public System.Collections.Generic.IReadOnlyList<string> DependsOn => System.Array.Empty<string>();

        /// <param name="createHost">Lua 宿主构造工厂（具体适配器对象由组合根创建；本段只认 ILuaHost）。</param>
        public LuaHostSegment(UnityEngine.GameObject hostObject,
            System.Func<UnityEngine.GameObject, ILuaHost> createHost)
        {
            _hostObject = hostObject;
            _createHost = createHost ?? throw new System.ArgumentNullException(nameof(createHost));
        }

        public UniTask InitializeAsync(SegmentContext context, CancellationToken ct)
        {
            // 资源归属＝本模块作用域（路线图 §4.3）：Lua env/回调随模块关闭或初始化失败释放。
            context.Scope.Register(new DelegatedDisposable(DisposeModuleResources));
            _lua = _createHost(_hostObject);
            context.Put<ILuaHost>(_lua);          // 模块侧只认端口（产品模块不引框架适配器）
            return UniTask.CompletedTask;
        }

        private void DisposeModuleResources()
        {
            _lua?.Shutdown();
            _lua = null;
        }

        /// <summary>关闭面：Lua 宿主资源已随 <see cref="SegmentContext.Scope"/> 释放。</summary>
        public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;
    }
}
