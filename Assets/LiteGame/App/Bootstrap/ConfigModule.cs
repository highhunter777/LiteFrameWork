using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteGame
{
    /// <summary>⑦ 配置：表投影数据源（UIFormCatalog/注册表填充依赖）。字节通道经 ②内容服务租约
    /// （C1-⑨：代次/引用统一——提取 bytes 即释放源资产，热更专项 §9）；候选校验→原子发布见 <see cref="ConfigService"/>。</summary>
    internal sealed class ConfigModule : IClientModule
    {
        public string Name => "Config";

        public UniTask InitializeAsync(ClientContext context, CancellationToken ct)
        {
            var content = context.Require<IContentService>();   // 依赖②——注册序即依赖序
            context.Put(new ConfigService((location, token) => LoadBytesViaContentLease(content, location, token)));
            return UniTask.CompletedTask;
        }

        public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;   // 配置重发布/事务归热更批

        /// <summary>经内容租约读表字节（TextAsset 提取 bytes 后即释放——复制数据不留源引用）。</summary>
        private static async UniTask<byte[]> LoadBytesViaContentLease(IContentService content, string location, CancellationToken ct)
        {
            var lease = await content.AcquireAsync<UnityEngine.TextAsset>(location, ct: ct);
            try { return lease.Asset.bytes; }
            finally { lease.Dispose(); }
        }
    }
}
