#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using System;

namespace LiteNet.Diagnostics
{
    /// <summary>弱网注入参数快照（开发面专用；release 不编译）。</summary>
    public readonly struct WeakNetProfile
    {
        public readonly bool Enabled;
        public readonly int LatencyMs;
        public readonly int JitterMs;
        public readonly int LossPermille;
        public readonly int DuplicatePermille;
        public readonly int ReorderPermille;
        public readonly uint Seed;

        public WeakNetProfile(bool enabled, int latencyMs, int jitterMs, int lossPermille,
            int duplicatePermille, int reorderPermille, uint seed)
        {
            Enabled = enabled;
            LatencyMs = latencyMs;
            JitterMs = jitterMs;
            LossPermille = lossPermille;
            DuplicatePermille = duplicatePermille;
            ReorderPermille = reorderPermille;
            Seed = seed;
        }

        public static readonly WeakNetProfile None = new WeakNetProfile(false, 0, 0, 0, 0, 0, 0);

        public bool IsValid =>
            LatencyMs >= 0 && JitterMs >= 0 &&
            LossPermille >= 0 && LossPermille <= 1000 &&
            DuplicatePermille >= 0 && DuplicatePermille <= 1000 &&
            ReorderPermille >= 0 && ReorderPermille <= 1000;
    }
}
#endif
