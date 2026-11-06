using LiteFramework;
using XLua;
using LiteFramework.UI;

namespace LiteGame
{
    /// <summary>UI 逻辑表注册表实现（kind="UI"）。</summary>
    public sealed class UILuaRegistry : LuaRegistry<LuaTable>, IUILuaRegistry
    {
        public UILuaRegistry() : base("UI") { }
    }
}
