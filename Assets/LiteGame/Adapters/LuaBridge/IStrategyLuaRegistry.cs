using LiteFramework;
using XLua;
using LiteFramework.UI;

namespace LiteGame
{
    /// <summary>壳策略覆盖注册表（TbStrategy.LuaPath，空 = 用 C# 默认实现，§4.4）。</summary>
    public interface IStrategyLuaRegistry : ILuaRegistry<LuaTable> { }
}
