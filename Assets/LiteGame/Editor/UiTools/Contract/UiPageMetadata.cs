namespace LiteGame.Editor.UiTools.Contracts
{
    /// <summary>
    /// 页面契约的页面元数据输入（来自 `tbuiform` 行；P1-3 的 `UiPageManifest` 可补 Owner/DesignRef）。
    /// 表列未落地前 `cacheStrategy` 固定默认 `Lru`，不另造第二口径。
    /// </summary>
    public sealed class UiPageMetadata
    {
        public int FormId { get; set; }
        public string LuaPath { get; set; }
        public int Layer { get; set; }
        public bool FullScreen { get; set; }
        public string CacheStrategy { get; set; } = UiPageContract.DefaultCacheStrategy;
        public string Owner { get; set; }
        public string DesignRef { get; set; }
    }
}
