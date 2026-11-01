using System;
using LiteFramework.UI;
using XLua; // lint-allow R12：页面数据包装（流程侧构造结算/菜单数据表；与 PageFlow 同判据）

namespace LiteGame.Modules.Ui
{
    /// <summary>Lua 侧数据包装：Lua 构造的数据表经本类包成 <see cref="IUIData"/>；
    /// 适配器回传时解包出原始 LuaTable——Lua 拿到的永远是自己认识的表，不是 C# 包装对象。</summary>
    public sealed class LuaUIData : IUIData
    {
        public LuaTable Table { get; }

        public LuaUIData(LuaTable table)
        {
            Table = table ?? throw new ArgumentNullException(nameof(table));
        }
    }
}