namespace LiteFramework
{
    /// <summary>
    /// 内容定位清单查询端口（<see cref="IContentService"/> 的可选扩展）：当前位置是否在**当前代次**的 资源清单内。
    /// </summary>
    public interface IContentLocationCatalog
    {
        /// <summary>location 是否在当前代次的资源清单内（不可寻址包以资源完整路径为 location）。</summary>
        bool IsLocationValid(string location);
    }
}