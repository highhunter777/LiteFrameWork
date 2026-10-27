#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using System;
using System.Text;

namespace LiteGame
{
    /// <summary>
    /// 联机读数环形窗（纯逻辑）：按 250ms 节流（4Hz）采 RTT/丢包，240 样本≈60s；
    /// 显示时降采样到指定列数并标注真实窗口。开发面投影只负责喂样与渲染。
    /// </summary>
    public sealed class NetHistory
    {
        public const int Capacity = 240;
        public const int SampleIntervalMs = 250;
        private const string Ramp = " .:-=+*#";

        private readonly int[] _rtt = new int[Capacity];
        private readonly int[] _loss = new int[Capacity];
        private readonly StringBuilder _rttLine = new StringBuilder(160);
        private readonly StringBuilder _lossLine = new StringBuilder(160);
        private int _head;
        private int _count;
        private long _lastSampleMs = long.MinValue;

        public int Count => _count;

        public void Reset()
        {
            _head = 0;
            _count = 0;
            _lastSampleMs = long.MinValue;
        }

        public bool Sample(long nowMs, int rttMs, int lossPermille)
        {
            if (_lastSampleMs != long.MinValue && nowMs - _lastSampleMs < SampleIntervalMs) return false;
            _lastSampleMs = nowMs;
            _rtt[_head] = Math.Max(0, rttMs);
            _loss[_head] = Math.Max(0, lossPermille);
            _head = (_head + 1) % Capacity;
            if (_count < Capacity) _count++;
            return true;
        }

        public bool TryFormat(int columns, out string rttLine, out string lossLine)
        {
            rttLine = null;
            lossLine = null;
            int columnCount = Math.Max(8, columns);
            if (_count < 8) return false;

            int window = Math.Min(_count, Capacity);
            int start = (_head - window + Capacity) % Capacity;
            int seconds = Math.Max(1, (int)Math.Round(window * SampleIntervalMs / 1000.0));

            _rttLine.Clear();
            _rttLine.Append("RTT ").Append(seconds).Append("s ");
            AppendSpark(_rttLine, _rtt, start, window, columnCount);
            _rttLine.Append(" min/avg/max ").Append(Min(_rtt, start, window)).Append('/')
                .Append(Avg(_rtt, start, window)).Append('/').Append(Max(_rtt, start, window)).Append("ms");

            _lossLine.Clear();
            _lossLine.Append("丢包 ").Append(seconds).Append("s ");
            AppendSpark(_lossLine, _loss, start, window, columnCount);
            _lossLine.Append(" avg/max ").Append(Avg(_loss, start, window)).Append('/')
                .Append(Max(_loss, start, window)).Append("‰");

            rttLine = _rttLine.ToString();
            lossLine = _lossLine.ToString();
            return true;
        }

        private static void AppendSpark(StringBuilder sb, int[] samples, int start, int window, int columns)
        {
            int levels = Ramp.Length - 1;
            int max = Max(samples, start, window);
            int safeMax = Math.Max(1, max);
            for (int i = 0; i < columns; i++)
            {
                int index = start + (int)((long)i * window / columns);
                int value = samples[(index) % Capacity];
                int level = Math.Min(levels, Math.Max(0, value * levels / safeMax));
                sb.Append(Ramp[level]);
            }
        }

        private static int Min(int[] samples, int start, int window)
        {
            int value = int.MaxValue;
            for (int i = 0; i < window; i++) value = Math.Min(value, samples[(start + i) % Capacity]);
            return value;
        }

        private static int Max(int[] samples, int start, int window)
        {
            int value = 0;
            for (int i = 0; i < window; i++) value = Math.Max(value, samples[(start + i) % Capacity]);
            return value;
        }

        private static int Avg(int[] samples, int start, int window)
        {
            long sum = 0;
            for (int i = 0; i < window; i++) sum += samples[(start + i) % Capacity];
            return (int)(sum / Math.Max(1, window));
        }
    }
}
#endif
