using System;

namespace LiteFramework
{
    /// <summary>
    /// 恢复策略（有界重试）：次数上限 + 指数退避。值由装配点给，不在执行器里写死。
    /// </summary>
    public sealed class RecoveryPolicy
    {
        /// <summary>默认：3 次尝试、200ms 起、×2 退避、2s 封顶。</summary>
        public static RecoveryPolicy Default { get; } = new RecoveryPolicy();

        public RecoveryPolicy(
            int maxAttempts = 3,
            TimeSpan? initialDelay = null,
            TimeSpan? maxDelay = null,
            double backoffFactor = 2.0)
        {
            if (maxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maxAttempts), "至少 1 次尝试");
            if (backoffFactor < 1.0) throw new ArgumentOutOfRangeException(nameof(backoffFactor), "退避系数不得小于 1");
            TimeSpan initial = initialDelay ?? TimeSpan.FromMilliseconds(200);
            TimeSpan max = maxDelay ?? TimeSpan.FromSeconds(2);
            if (initial <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(initialDelay), "初始退避必须为正");
            if (max < initial) throw new ArgumentOutOfRangeException(nameof(maxDelay), "封顶不得小于初始退避");

            MaxAttempts = maxAttempts;
            InitialDelay = initial;
            MaxDelay = max;
            BackoffFactor = backoffFactor;
        }

        public int MaxAttempts { get; }

        public TimeSpan InitialDelay { get; }

        public TimeSpan MaxDelay { get; }

        public double BackoffFactor { get; }

        /// <summary>第 <paramref name="attempt"/> 次失败后的退避（attempt 从 1 起：initial × factor^(attempt-1)，封顶）。</summary>
        public TimeSpan DelayFor(int attempt)
        {
            if (attempt < 1) throw new ArgumentOutOfRangeException(nameof(attempt));
            double ms = InitialDelay.TotalMilliseconds * Math.Pow(BackoffFactor, attempt - 1);
            if (ms > MaxDelay.TotalMilliseconds) ms = MaxDelay.TotalMilliseconds;
            return TimeSpan.FromMilliseconds(ms);
        }
    }
}
