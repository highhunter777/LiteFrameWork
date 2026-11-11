using System;

namespace LiteGameNext.Platform.Ui
{
    /// <summary>页面层：数值即 Canvas 排序（Screen 最底、System 最顶）。</summary>
    public enum PageLayer
    {
        /// <summary>普通整页。</summary>
        Screen = 0,

        /// <summary>模态页（射线遮蔽、Back 优先关闭）。</summary>
        Modal = 1,

        /// <summary>浮层（非模态、通常非全屏）。</summary>
        Overlay = 2,

        /// <summary>系统页（加载/错误等，Back 最优先）。</summary>
        System = 3,
    }

    /// <summary>页面关闭后的缓存策略：Lru 可被淘汰、Resident 常驻、DestroyOnClose 立即销毁。</summary>
    public enum PageCachePolicy
    {
        /// <summary>进缓存，可被预算淘汰。</summary>
        Lru = 0,

        /// <summary>进缓存，永不淘汰（系统页/重开代价高的页）。</summary>
        Resident = 1,

        /// <summary>关闭即销毁，不进缓存。</summary>
        DestroyOnClose = 2,
    }

    /// <summary>页面元数据（页面类上的静态事实）：prefab 地址、层、是否全屏、缓存策略；无全局注册表。</summary>
    public sealed record PageSpec
    {
        /// <summary>构造完整元数据；空白地址属程序员错误，直接抛。</summary>
        public PageSpec(string address, PageLayer layer, bool fullScreen, PageCachePolicy cachePolicy)
        {
            if (string.IsNullOrWhiteSpace(address))
                throw new ArgumentException("页面地址不能为空（PageSpec.Address = prefab 地址）", nameof(address));

            Address = address;
            Layer = layer;
            FullScreen = fullScreen;
            CachePolicy = cachePolicy;
        }

        /// <summary>默认整页：Screen + 全屏 + Lru。</summary>
        public PageSpec(string address) : this(address, PageLayer.Screen, true, PageCachePolicy.Lru)
        {
        }

        /// <summary>prefab 地址（内容系统 location）；同地址即同一页面身份。</summary>
        public string Address { get; }

        /// <summary>页面层。</summary>
        public PageLayer Layer { get; }

        /// <summary>是否全屏（全屏页计入输入阻断）。</summary>
        public bool FullScreen { get; }

        /// <summary>关闭后的缓存策略。</summary>
        public PageCachePolicy CachePolicy { get; }
    }
}
