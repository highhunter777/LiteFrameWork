using System;
using LiteFramework;
using LiteGame.Modules.Ui;

namespace LiteGame
{
    /// <summary>
    /// Lua 桥装订面的实现（`ILuaUiBinding` 端口）：装配根一次构造，Ui 段只认端口。
    /// 三件共用同一 Lua 宿主与同一 UI 注册表实例（页面逻辑表由 RegistryFiller 填充）。
    /// </summary>
    public sealed class LuaUiBinding : ILuaUiBinding
    {
        private readonly LuaComponent _lua;

        public LuaUiBinding(LuaComponent lua, IUILuaRegistry registry)
        {
            _lua = lua ?? throw new ArgumentNullException(nameof(lua));
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            LogicFactory = new LuaUiLogicFactory(_lua, registry);
            PageData = new LuaPageDataFactory(() => _lua.Env);
        }

        /// <inheritdoc />
        public IUiLogicFactory LogicFactory { get; }

        /// <inheritdoc />
        public IPageDataFactory PageData { get; }

        /// <inheritdoc />
        public void BindTextFacade(Func<ILocalizationService> localeProvider) => Bridge.BindText(localeProvider);
    }
}
