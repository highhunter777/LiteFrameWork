#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using System;
using System.Text;

namespace LiteGame
{
    /// <summary>
    /// 联机读数环形窗（240 个采样 ≈ 60s@4Hz）：RTT / 丢包 sparkline + min/avg/max。
    /// 只在有对局样本时采样；换局 Reset，避免把上一局曲线带进下一局。
    /// </summary>
    internal sealed class DevHudNetHistory
    {
        public const int Capacity = 240;
        private const string Ramp = " .:-=+*#";

        private readonly int[] _rtt = new int[Capacity];
        private readonly int[] _loss = new int[Capacity];
        private readonly StringBuilder _rttLine = new StringBuilder(160);
        private readonly StringBuilder _lossLine = new StringBuilder(160);
        private int _head;
        private int _count;

        public int Count => _count;

        public void Reset()
        {
            _head = 0;
            _count = 0;
        }

        public void Sample(int rttMs, int lossPermille)
        {
            _rtt[_head] = Math.Max(0, rttMs);
            _loss[_head] = Math.Max(0, lossPermille);
            _head = (_head + 1) % Capacity;
            if (_count < Capacity) _count++;
        }

        /// <summary>格式两行（样本不足 8 个返回 false）。</summary>
        public bool TryFormat(int width, out string rttLine, out string lossLine)
        {
            rttLine = null;
            lossLine = null;
            int n = Math.Min(_count, Math.Max(8, width));
            if (n < 8) return false;

            int start = (_head - n + Capacity) % Capacity;
            int rttSum = 0, rttMin = int.MaxValue, rttMax = 0;
            int lossSum = 0, lossMax = 0;
            for (int i = 0; i < n; i++)
            {
                int index = (start + i) % Capacity;
                int rtt = _rtt[index];
                int loss = _loss[index];
                rttSum += rtt;
                if (rtt < rttMin) rttMin = rtt;
                if (rtt > rttMax) rttMax = rtt;
                lossSum += loss;
                if (loss > lossMax) lossMax = loss;
            }

            _rttLine.Clear();
            _rttLine.Append("RTT 60s ");
            AppendSpark(_rttLine, _rtt, start, n, rttMax);
            _rttLine.Append(" min/avg/max ").Append(rttMin).Append('/')
                .Append(rttSum / n).Append('/').Append(rttMax).Append("ms");

            _lossLine.Clear();
            _lossLine.Append("丢包 60s ");
            AppendSpark(_lossLine, _loss, start, n, Math.Max(lossMax, 10));
            _lossLine.Append(" avg/max ").Append(lossSum / n).Append('/').Append(lossMax).Append("‰");

            rttLine = _rttLine.ToString();
            lossLine = _lossLine.ToString();
            return true;
        }

        private void AppendSpark(StringBuilder sb, int[] samples, int start, int n, int max)
        {
            int levels = Ramp.Length - 1;
            int safeMax = Math.Max(1, max);
            for (int i = 0; i < n; i++)
            {
                int value = samples[(start + i) % Capacity];
                int level = Math.Min(levels, Math.Max(0, value * levels / safeMax));
                sb.Append(Ramp[level]);
            }
        }
    }
}
#endif
