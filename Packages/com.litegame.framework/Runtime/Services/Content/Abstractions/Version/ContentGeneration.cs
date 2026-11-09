using System;

namespace LiteFramework
{
    /// <summary>
    /// 内容代次：同一 location 在不同代次下是**不同资源**——候选与当前隔离、旧使用者不从全局 Current 混取新内容的键维度。
    /// 本类型是代次的**键身份**（ReleaseId + 单调 Value）；完整代次对象（目录/脚本集合/快照/依赖 ID）在激活事务上构建。
    /// </summary>
    public readonly struct ContentGeneration : IEquatable<ContentGeneration>
    {
        /// <summary>内置内容的发布身份（无热更兜底；激活记录初值同源）。</summary>
        public const string BuiltinReleaseId = "builtin";

        /// <summary>发布身份（ReleaseId：一次不可变内容发布；内置内容为 <see cref="BuiltinReleaseId"/>）。</summary>
        public string ReleaseId { get; }

        /// <summary>同 Release 内单调代次（内置/初始 = 0；候选激活递增）。</summary>
        public ulong Value { get; }

        /// <summary>内置代次（无热更、始终可用——启动与恢复的兜底身份）。</summary>
        public static ContentGeneration Default { get; } = new ContentGeneration(BuiltinReleaseId, 0);

        public ContentGeneration(string releaseId, ulong value)
        {
            ReleaseId = releaseId ?? throw new ArgumentNullException(nameof(releaseId));
            Value = value;
        }

        public bool Equals(ContentGeneration other)
            => Value == other.Value && string.Equals(ReleaseId, other.ReleaseId, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is ContentGeneration other && Equals(other);

        public override int GetHashCode()
            => (ReleaseId == null ? 0 : StringComparer.Ordinal.GetHashCode(ReleaseId)) ^ Value.GetHashCode();

        public override string ToString() => $"{ReleaseId}#{Value}";
    }
}
