namespace LiteGame.Editor.UITools.Contracts
{
    /// <summary>
    /// 页面契约的页面元数据输入（来自 `tbuiform` 行；P1-3 的 `UIPageManifest` 可补 Owner/DesignRef）。
    /// 表列未落地前 `cacheStrategy` 固定默认 `Lru`，不另造第二口径。
    /// </summary>
    public sealed class UIPageMetadata
    {
        public int FormId { get; set; }
        public int Layer { get; set; }
        public bool FullScreen { get; set; }
        public string CacheStrategy { get; set; } = UIPageContract.DefaultCacheStrategy;
        public string Owner { get; set; }
        public string DesignRef { get; set; }

        /// <summary>草稿契约（未登记 tbuiform）：formId=0、使用草稿默认层/全屏。</summary>
        public bool IsDraft { get; set; }
    }
}
