#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using System;

namespace LiteGame
{
    /// <summary>投影薄适配：窗口/节流/降采样逻辑在 App 层 NetHistory（可测）。</summary>
    internal sealed class DevHudNetHistory
    {
        private readonly NetHistory _inner = new NetHistory();

        public int Count => _inner.Count;

        public void Reset() => _inner.Reset();

        public void Sample(int rttMs, int lossPermille) => _inner.Sample(NowMs(), rttMs, lossPermille);

        public bool TryFormat(int width, out string rttLine, out string lossLine)
            => _inner.TryFormat(width, out rttLine, out lossLine);

        private static long NowMs()
            => (long)(System.Diagnostics.Stopwatch.GetTimestamp() * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
    }
}
#endif
