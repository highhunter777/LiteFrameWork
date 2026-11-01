using System.Collections.Generic;
using LiteFramework.UI;

namespace LiteGame.Modules.Ui
{
    /// <summary>
    /// 页面目录端口（Ui 域产出，实现＝生成物 <see cref="UiPageCatalog"/>）：壳契约
    /// <see cref="IPageCatalog"/>（按 id 取件）＋ 目录查询面（全量行 / lua_path 反查）。
    ///
    /// 为什么需要：流程段要"按 lua_path 寻址页面"与"取全部页面定义"，但页面目录本体归 Ui 域。
    /// 流程段与 Lua 桥只经本口取目录事实，不直取生成物类型——目录换生成形态时不改消费方。
    /// </summary>
    public interface IPageDirectory : IPageCatalog
    {
        /// <summary>全部页面定义（按 Id 升序；注册表填充与预载按它逐行处理）。</summary>
        IReadOnlyList<PageDefinition> All { get; }

        /// <summary>lua_path → 页面 id（如 `UI.Page_Lobby`）；未登记返回 -1。</summary>
        int Resolve(string luaPath);
    }
}
