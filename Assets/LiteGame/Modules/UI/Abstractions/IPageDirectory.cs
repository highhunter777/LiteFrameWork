using System.Collections.Generic;
using LiteFramework.UI;

namespace LiteGame.Modules.UI
{
    /// <summary>
    /// 页面目录端口（UI 域产出，实现＝生成物 UIPageCatalog）：壳契约 IPageCatalog（按 id 取件）＋ 目录查询面（全量行 / lua_path 反查）。
    /// </summary>
    public interface IPageDirectory : IPageCatalog
    {
        /// <summary>全部页面定义（按 Id 升序；注册表填充与预载按它逐行处理）。</summary>
        IReadOnlyList<PageDefinition> All { get; }

        /// <summary>lua_path → 页面 id（如 `UI.Page_Lobby`）；未登记返回 -1。</summary>
        int Resolve(string luaPath);
    }
}
