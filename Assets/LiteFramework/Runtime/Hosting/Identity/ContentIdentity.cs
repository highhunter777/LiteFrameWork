using System;

namespace LiteFramework
{
    /// <summary>
    /// 内容身份（ContentIdentity）：当前已确认内容代次的可诊断身份面。
    /// 由组合根在内容/配置装配后绑定；未绑定 = <see cref="Empty"/>（内置内容）。
    /// </summary>
    public readonly struct ContentIdentity
    {
        public string ReleaseId { get; }
        public ulong Generation { get; }
        public long Revision { get; }
        public uint ConfigDigestBase { get; }
        public string RootMotionDigest { get; }

        public ContentIdentity(string releaseId, ulong generation, long revision,
            uint configDigestBase, string rootMotionDigest)
        {
            ReleaseId = string.IsNullOrEmpty(releaseId) ? ContentGeneration.BuiltinReleaseId : releaseId;
            Generation = generation;
            Revision = revision;
            ConfigDigestBase = configDigestBase;
            RootMotionDigest = rootMotionDigest ?? string.Empty;
        }

        public bool IsBuiltin => string.Equals(ReleaseId, ContentGeneration.BuiltinReleaseId, StringComparison.Ordinal);

        public static ContentIdentity Empty { get; } = new ContentIdentity(
            ContentGeneration.BuiltinReleaseId, 0, 0, 0, string.Empty);

        public override string ToString()
            => ReleaseId + "#" + Generation + " rev=" + Revision + " cfg=" + ConfigDigestBase.ToString("x8");
    }
}
