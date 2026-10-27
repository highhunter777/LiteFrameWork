using System;
using LiteNet.Diagnostics;

namespace LiteNet
{
    /// <summary>
    /// RTT/丢包探针（RoomClient 的行内件，M2 拆分）：1Hz Unreliable 心跳，服务器原样回显
    /// client_time——本端据此配对算 RTT，并按滚动窗口统计丢包千分比。
    /// 纯观测面（表现/诊断），不参与任何游戏逻辑或准入。
    /// </summary>
    internal sealed class NetworkQualityProbe
    {
        /// <summary>探针发送周期（毫秒）。</summary>
        public const long IntervalMs = 1000;

        /// <summary>探针滚动窗口（丢包统计分母——越大越平滑、越钝；须 ≥ 1）。</summary>
        public const int Window = 8;

        private readonly long[] _sentAt = new long[Window];    // 环形：探针发送时刻（client_time 兼探针 id）
        private readonly bool[] _echoed = new bool[Window];    // 对应探针是否已收回声
        private int _write;                                    // 下一写入下标
        private int _count;                                    // 窗口内有效探针数（≤ Window）
        private long _lastMs = long.MinValue;                  // 上次发探针时刻（节流）
        private int _rttMs = NetworkQuality.NoSample;          // 最近一次回声推得的 RTT

        /// <summary>窗口清零（相位切换/进房起新一轮统计）。</summary>
        public void Reset()
        {
            _count = 0;
            _write = 0;
            _lastMs = long.MinValue;
            _rttMs = NetworkQuality.NoSample;
            Array.Clear(_sentAt, 0, _sentAt.Length);
            Array.Clear(_echoed, 0, _echoed.Length);
        }

        /// <summary>节流发一枚探针（到点才发）；<paramref name="send"/> 收到本次 client_time 并负责实际发送。</summary>
        public void Pump(long now, Action<long> send)
        {
            if (_lastMs != long.MinValue && now - _lastMs < IntervalMs) return;
            _lastMs = now;
            send(now);

            _sentAt[_write] = now;
            _echoed[_write] = false;
            _write = (_write + 1) % Window;
            if (_count < Window) _count++;
        }

        /// <summary>回声配对：client_time 命中在途探针 → 记 RTT（未命中的回声忽略——重发/旧回声）。</summary>
        public void ObserveEcho(long clientTime, long now)
        {
            for (int i = 0; i < _count; i++)
            {
                int idx = (_write - 1 - i + Window * 2) % Window;
                if (_sentAt[idx] != clientTime || _echoed[idx]) continue;
                _echoed[idx] = true;
                int rtt = (int)(now - clientTime);
                _rttMs = rtt < 0 ? 0 : rtt;
                return;
            }
        }

        /// <summary>丢包千分比：窗口内"未回声"占比（无样本 = <see cref="NetworkQuality.NoSample"/>）。</summary>
        public int LossPermille()
        {
            if (_count == 0) return NetworkQuality.NoSample;
            int lost = 0;
            for (int i = 0; i < _count; i++)
            {
                int idx = (_write - 1 - i + Window * 2) % Window;
                if (!_echoed[idx]) lost++;
            }
            return lost * 1000 / _count;
        }

        /// <summary>质量快照（含连接态门——离线不显示数值）。</summary>
        public NetworkQuality Quality(bool connected) => new NetworkQuality(_rttMs, LossPermille(), connected);
    }
}
