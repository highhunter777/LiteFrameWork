namespace LiteClient
{
    /// <summary>
    /// 内容定位清单查询端口（<see cref="IContentService"/> 的可选扩展）：当前位置是否在**当前代次**的
    /// 资源清单内。
    ///
    /// 为什么需要：健康探针里"入口资源可加载性"必须区分两件事——
    /// **该资源不属于当前内容**（跳过，不构成失败）与 **属于当前内容但加载不出来**（硬失败）。
    /// 只有能问清单才不至于把"内置代次没有候选专属资源"误判成启动失败。
    ///
    /// 契约：**初始化完成后才可查询**（清单未加载时无意义）。
    /// </summary>
    public interface IContentLocationCatalog
    {
        /// <summary>location 是否在当前代次的资源清单内（不可寻址包以资源完整路径为 location）。</summary>
        bool IsLocationValid(string location);
    }
}