using System;
using LiteFramework.UI;

namespace LiteGame.Modules.UI
{
    /// <summary>
    /// lua_path → 页面 id（生成页面目录反查，未登记返回 -1）。
    /// </summary>
    public static class UIPageResolver
    {
        public static int Resolve(string luaPath)
        {
            if (string.IsNullOrEmpty(luaPath)) return -1;
            foreach (PageDefinition page in UIPageCatalog.All)
                if (string.Equals(page.LuaPath, luaPath, StringComparison.Ordinal)) return page.Id;
            return -1;
        }
    }
}