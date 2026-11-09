using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteFramework
{
    /// <summary>
    /// 内容服务端口：用可注入 IContentService 取代静态所有权。
    /// </summary>
    public interface IContentService
    {
        /// <summary>初始化（幂等）。</summary>
        UniTask InitializeAsync(CancellationToken ct = default);

        /// <summary>内容服务是否已就绪（初始化成功后为真；关闭后为假）。</summary>
        bool IsInitialized { get; }

        /// <summary>获取资源租约（引用计数 +1；失败抛含 location 的异常；ct 取消贯穿加载）。
        /// <paramref name="generation"/> 固定本次获取的**内容代次**（加载/缓存键包含代次——
        /// 默认 = 当前代；旧 Scope 应显式携带自己的代次，不从全局 Current 混取新内容）。</summary>
        UniTask<AssetLease<T>> AcquireAsync<T>(string location, ContentGeneration generation = default, CancellationToken ct = default) where T : class;

        /// <summary>
        /// 按 tag 列出内容路径（如 "ui" → 全部 UI 资产路径）。
        /// </summary>
        IReadOnlyList<string> ListAssetPathsByTag(string tag);

        /// <summary>优雅关闭：释放全部剩余租约与缓存（幂等）。</summary>
        UniTask ShutdownAsync(CancellationToken ct = default);
    }
}
