using LiteFramework;
using XLua;
using LiteFramework.UI;

namespace LiteGame
{
    /// <summary>内容处理器注册表实现（kind="Content"）。</summary>
    public sealed class ContentLuaRegistry : LuaRegistry<LuaTable>, IContentLuaRegistry
    {
        public ContentLuaRegistry() : base("Content") { }
    }
}
