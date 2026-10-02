using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteFramework
{
    /// <summary>
    /// 异步发布变体：候选构造/校验涉及异步 IO（读表/反序列化）时使用。
    /// 发布语义与同步版一致：校验失败保留旧版。
    ///
    /// 本扩展返回 <c>UniTask</c>，故随 `LiteFramework.Async` 落位；同步的
    /// <see cref="ConfigSnapshotService{TSnapshot}"/> 本体**零异步依赖**，留在 Core（S2 契约层）。
    /// 本扩展当前**无生产消费者**（仅 L1 用例覆盖）——保留为契约面。
    /// </summary>
    public static class ConfigSnapshotPublishExtensions
    {
        /// <summary>异步构造候选 + 校验 + 原子发布（先验证后原子提交）。</summary>
        public static async UniTask<ulong> PublishAsync<TSnapshot>(
            this ConfigSnapshotService<TSnapshot> service,
            Func<CancellationToken, UniTask<TSnapshot>> buildCandidate,
            CancellationToken ct = default)
            where TSnapshot : class
        {
            TSnapshot candidate = await buildCandidate(ct);
            return service.Publish(candidate);
        }
    }
}
