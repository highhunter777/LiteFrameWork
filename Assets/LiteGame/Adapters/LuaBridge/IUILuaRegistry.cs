using LiteFramework;
using XLua;
using LiteFramework.UI;

namespace LiteGame
{
    /// <summary>UI 逻辑表注册表（PageDefinition.LuaPath → ui/ 下逻辑表；UI 服务装配经生命周期桥消费）。</summary>
    public interface IUILuaRegistry : ILuaRegistry<LuaTable> { }
}
