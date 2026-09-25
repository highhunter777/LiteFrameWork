using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteFramework
{
    /// <summary>
    /// 配置快照服务（《商业级通用客户端框架总设计》§9 配置/版本 + §4 原则 6"先验证后原子提交"）：
    /// 配置以**不可变快照**形态发布——消费方持有引用即持有完整一致视图；
    /// 发布 = 候选校验通过后**原子替换**当前快照（lock 互斥的单写者发布点），失败保留已发布版本（§4 原则 10）。
    ///
    /// 发布流程（§10.1 同环境内容更新的运行态部分）：
    /// <c>PublishAsync(候选内容) → 候选校验（validate 委托）→ 校验失败保留旧版并上抛 →
    /// 校验通过 → lock 互斥原子替换 → 旧快照由消费方按需释放</c>。
    ///
    /// 版本标识：每次发布 <see cref="Version"/> 单调递增（ulong）——消费方比对版本即可发现内容切换，
    /// 缓存页在重打开前应用最新快照（Covered 页也更新或记录脏版本，UI 框架总设计 §9）。
    ///
    /// 快照本体 <typeparamref name="TSnapshot"/> 由消费方定义（ConfigSnapshot 结构含表数据/哈希/内容版本）。
    /// </summary>
    /// <typeparam name="TSnapshot">快照本体类型（引用类型——原子替换引用即可）。</typeparam>
    public sealed class ConfigSnapshotService<TSnapshot> where TSnapshot : class
    {
        private readonly object _gate = new object();
        private readonly Func<TSnapshot, string> _validate;   // 候选校验委托：null=通过，非 null=拒绝原因
        private TSnapshot _published;
        private ulong _version;

        /// <summary>当前已发布快照（不可变契约：消费方不得改写其内容）。</summary>
        public TSnapshot Current { get { lock (_gate) return _published; } }

        /// <summary>当前版本（每次成功发布 +1；0 = 尚未发布）。</summary>
        public ulong Version { get { lock (_gate) return _version; } }

        /// <param name="validate">候选校验委托：返回 null = 通过；返回非 null 字符串 = 拒绝原因（原样上抛给调用方）。</param>
        public ConfigSnapshotService(Func<TSnapshot, string> validate)
        {
            _validate = validate ?? throw new ArgumentNullException(nameof(validate));
        }

        /// <summary>
        /// 原子发布：候选先过 <paramref name="validate"/> 校验，失败保留已发布版本并抛
        /// <see cref="InvalidOperationException"/>（携带拒绝原因）；成功则版本 +1 并原子替换。
        /// </summary>
        public ulong Publish(TSnapshot candidate)
        {
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));

            string rejectReason;
            lock (_gate)
            {
                rejectReason = _validate(candidate);
            }
            if (rejectReason != null)
                throw new InvalidOperationException($"配置快照校验失败：{rejectReason}");

            lock (_gate)
            {
                _published = candidate;
                _version++;
                return _version;
            }
        }
    }

    /// <summary>
    /// 异步发布变体：候选构造/校验涉及异步 IO（读表/反序列化）时使用。
    /// 发布语义与同步版一致：校验失败保留旧版。
    /// </summary>
    public static class ConfigSnapshotPublishExtensions
    {
        /// <summary>异步构造候选 + 校验 + 原子发布（§10.1"先验证后原子提交"的异步形态）。</summary>
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
