namespace LiteGameNext.Platform.Ui.Content
{
    /// <summary>P0 占位整页：无业务逻辑，只证明「prefab → PageBase → UiRuntime 栈」通路；P1 由大厅页替换。</summary>
    public sealed class PlaceholderPage : PageBase
    {
        private const string Title = "LiteGame Next";
        private const string HintFormat = "P0 占位页 · 第 {0} 次展示";

        /// <summary>页面元数据：地址是 P0 编辑器装载用的 prefab 资源路径（Player 换 YooAsset location）。</summary>
        public static readonly PageSpec Spec = new PageSpec(
            "Assets/LiteGame/Next/Runtime/Platform/Ui/Content/PlaceholderPage.prefab",
            PageLayer.Screen, fullScreen: true, PageCachePolicy.Lru);

        /// <summary>展示次数：同一实例被缓存复用时递增，供启动/缓存用例观察。</summary>
        public int ShowCount { get; private set; }

        /// <summary>每次展示刷新标题与提示文本。</summary>
        protected override void OnShow()
        {
            ShowCount++;
            SetText("Txt_Title", Title);
            SetText("Txt_Hint", string.Format(HintFormat, ShowCount));
        }
    }
}
