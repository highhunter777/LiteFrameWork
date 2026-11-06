using System;

namespace LiteFramework
{
    /// <summary>
    /// 配置快照服务（"先验证后原子提交"）： 配置以**不可变快照**形态发布——消费方持有引用即持有完整一致视图； 发布 = 候选校验通过后**原子替换**当前快照（lock 互斥的单写者发布点），失败保留已发布版本。
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

            // 校验与发布在同一临界区：避免两个并发发布者"各自校验、按不同顺序发布"的竞态。
            lock (_gate)
            {
                string rejectReason = _validate(candidate);
                if (rejectReason != null)
                    throw new InvalidOperationException($"配置快照校验失败：{rejectReason}");

                _published = candidate;
                _version++;
                return _version;
            }
        }
    }
}
