using LiteFramework;
using XLua;
using LiteFramework.UI;

namespace LiteGame
{
    /// <summary>内容处理器注册表（TbContentEntry.Entry → content/ 下处理器表）。</summary>
    public interface IContentLuaRegistry : ILuaRegistry<LuaTable> { }
}
