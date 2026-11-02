using System;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteFramework.UI;
using LiteGame.Modules.UI;

namespace LiteGame
{
    /// <summary>
    /// 页面逻辑工厂的实现（`IUILogicFactory` 端口 → Lua 生命周期桥）：注册表按 lua_path 取模块表，
    /// 构造 <see cref="LuaBehaviourAdapter"/> 作为页面实例工厂。
    ///
    /// env 每次现取（`LuaComponent.Env`）——DevReload 重建 env 后旧对象全失效，缓存 env 会把旧表打进死环境。
    /// </summary>
    public sealed class LuaUILogicFactory : IUILogicFactory
    {
        private readonly LuaComponent _lua;
        private readonly IUILuaRegistry _registry;

        public LuaUILogicFactory(LuaComponent lua, IUILuaRegistry registry)
        {
            _lua = lua ?? throw new ArgumentNullException(nameof(lua));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        /// <inheritdoc />
        public IUIFormLogic Create(PageDefinition page, IConfirmDialog confirm)
        {
            if (page == null) throw new ArgumentNullException(nameof(page));
            Func<string, string, string, string, UniTask<bool>> show =
                confirm != null ? new Func<string, string, string, string, UniTask<bool>>(confirm.ShowAsync) : null;
            return new LuaBehaviourAdapter(_lua.Env, _registry.Get(page.LuaPath), show);
        }
    }
}
