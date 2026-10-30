using System;
using LiteFramework.UI;

namespace LiteGame.UI
{
    /// <summary>
    /// lua_path → 页面 id（生成页面目录反查，未登记返回 -1）。
    /// 页面寻址的单源解析口——产品流程（PageFlow/Preload）与 Lua 桥门面都经本口；
    /// 目录本体是生成物 <see cref="UiPageCatalog"/>（`#uiform` 单源），magic id 不进脚本。
    /// </summary>
    public static class UiPageResolver
    {
        public static int Resolve(string luaPath)
        {
            if (string.IsNullOrEmpty(luaPath)) return -1;
            foreach (PageDefinition page in UiPageCatalog.All)
                if (string.Equals(page.LuaPath, luaPath, StringComparison.Ordinal)) return page.Id;
            return -1;
        }
    }
}