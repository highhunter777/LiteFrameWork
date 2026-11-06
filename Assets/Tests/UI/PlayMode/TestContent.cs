using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteGame.Tests.UI.PlayMode
{
    /// <summary>PlayMode 用例的内容服务单例（替代退役的 AssetService 门面）：首用初始化、后续复用同一包。</summary>
    internal static class TestContent
    {
        private static YooAssetContentService s_content;

        /// <summary>已初始化的内容服务（<see cref="InitAsync"/> 之后可用）。</summary>
        public static YooAssetContentService Content => s_content;

        /// <summary>首次调用初始化编辑器模拟包；后续调用无操作。</summary>
        public static async UniTask InitAsync()
        {
            if (s_content != null) return;
            var content = new YooAssetContentService(
                new FileSystem(new UnityPathProvider(), new NewtonsoftJsonSerializer()));
            await content.InitializeAsync();
            s_content = content;
        }

        /// <summary>加载资产并立即归还句柄（与旧门面同语义；失败含 location）。</summary>
        public static async UniTask<T> LoadAssetAsync<T>(string location, CancellationToken ct = default)
            where T : UnityEngine.Object
        {
            var content = s_content ?? throw new InvalidOperationException("TestContent 未初始化——先 InitAsync");
            var lease = await content.AcquireAsync<T>(location, ct: ct);
            try { return lease.Asset; }
            finally { lease.Dispose(); }
        }
    }
}
