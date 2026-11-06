using LiteFramework;
using XLua;
using LiteFramework.UI;

namespace LiteGame
{
    /// <summary>壳策略覆盖注册表实现（kind="Strategies"）。</summary>
    public sealed class StrategyLuaRegistry : LuaRegistry<LuaTable>, IStrategyLuaRegistry
    {
        public StrategyLuaRegistry() : base("Strategies") { }
    }
}
