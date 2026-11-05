using System;
using System.Collections.Generic;

namespace LiteFramework.UI
{
    /// <summary>
    /// 页面目录契约：壳只依赖"按 id 取页面定义"这一件事。生产实现 = 生成物
    /// <c>UIPageCatalog</c>；测试可注入替身，不必拉起配置链路。
    /// </summary>
    public interface IPageCatalog
    {
        PageDefinition Get(int id);
    }
}
