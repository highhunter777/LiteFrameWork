using System.Collections.Generic;
using LiteFramework.UI;

namespace LiteGame.Modules.UI
{
    /// <summary>
    /// 页面目录端口（UI 域产出，实现＝生成物 UIPageCatalog）：壳契约 IPageCatalog（按 id 取件）＋ 全量行。
    /// </summary>
    public interface IPageDirectory : IPageCatalog
    {
        /// <summary>全部页面定义（按 Id 升序；注册表填充与预载按它逐行处理）。</summary>
        IReadOnlyList<PageDefinition> All { get; }

    }
}
