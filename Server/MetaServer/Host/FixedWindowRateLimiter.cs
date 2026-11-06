using System;
using System.Collections.Generic;

namespace MetaServer
{
    /// <summary>
    /// 固定窗口请求限流器（**无鉴权面的兜底**：游客登录是全服务唯一"不花任何凭据就能落地一条账号文档" 的端点，没有它，匿名者枚举 deviceId 即可让 accounts 集合无界增长）。
    /// </summary>
    public sealed class FixedWindowRateLimiter
    {
        private struct Window
        {
            public long StartMs;
            public int Count;
        }

        private readonly object _gate = new object();
        private readonly Dictionary<string, Window> _windows = new Dictionary<string, Window>(StringComparer.Ordinal);
        private readonly int _permitLimit;
        private readonly long _windowMs;
        private readonly int _capacity;
        private readonly Func<long> _nowMs;
        private long _lastSweepMs;
        private long _rejected;

        /// <param name="permitLimit">每窗口允许的请求数（&gt;=1；0 = 调用方不创建本对象 = 功能关闭）。</param>
        /// <param name="windowMs">窗口长度（毫秒）。</param>
        /// <param name="capacity">键表容量上限（&gt;=1）。</param>
        /// <param name="nowMs">单调毫秒时钟（注入——L1 用虚拟时钟推窗口，不 sleep）。</param>
        public FixedWindowRateLimiter(int permitLimit, long windowMs, int capacity, Func<long> nowMs)
        {
            if (permitLimit < 1) throw new ArgumentOutOfRangeException(nameof(permitLimit), permitLimit, "每窗口许可数必须 >= 1");
            if (windowMs < 1) throw new ArgumentOutOfRangeException(nameof(windowMs), windowMs, "窗口必须 >= 1ms");
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "键表容量必须 >= 1");
            _permitLimit = permitLimit;
            _windowMs = windowMs;
            _capacity = capacity;
            _nowMs = nowMs ?? throw new ArgumentNullException(nameof(nowMs));
        }

        /// <summary>每窗口许可数（诊断）。</summary>
        public int PermitLimit
        {
            get { return _permitLimit; }
        }

        /// <summary>窗口长度（毫秒，诊断）。</summary>
        public long WindowMs
        {
            get { return _windowMs; }
        }

        /// <summary>被拒总数（诊断——运维据此判断"是不是有人在刷"）。</summary>
        public long Rejected
        {
            get { lock (_gate) return _rejected; }
        }

        /// <summary>当前键表条数（诊断）。</summary>
        public int KeyCount
        {
            get { lock (_gate) return _windows.Count; }
        }

        /// <summary>
        /// 取一次许可。<paramref name="retryAfterMs"/> = 建议重试等待（窗口剩余，仅拒绝时有意义）。
        /// </summary>
        public bool TryAcquire(string key, out long retryAfterMs)
        {
            retryAfterMs = 0;
            if (string.IsNullOrEmpty(key)) key = "unknown";     // 探测不到源地址：归一个共享桶（仍受限）

            lock (_gate)
            {
                long now = _nowMs();
                SweepLocked(now);

                if (!_windows.TryGetValue(key, out Window window) || now - window.StartMs >= _windowMs)
                {
                    if (!_windows.ContainsKey(key) && _windows.Count >= _capacity)
                    {
                        // 键表满且没有可回收的过期键：**拒绝新键**（fail-closed，见类注释）
                        _rejected++;
                        retryAfterMs = _windowMs;
                        return false;
                    }
                    _windows[key] = new Window { StartMs = now, Count = 1 };
                    return true;
                }

                if (window.Count >= _permitLimit)
                {
                    _rejected++;
                    retryAfterMs = _windowMs - (now - window.StartMs);
                    if (retryAfterMs < 0) retryAfterMs = 0;
                    return false;
                }

                window.Count++;
                _windows[key] = window;
                return true;
            }
        }

        /// <summary>清掉过期窗口（摊销：最多每秒一次；撞容量上限时强制清一次）。调用方须持有 <c>_gate</c>。</summary>
        private void SweepLocked(long now)
        {
            if (_windows.Count == 0) return;
            if (_windows.Count < _capacity && now - _lastSweepMs < SweepIntervalMs) return;
            _lastSweepMs = now;

            List<string> expired = null;
            foreach (KeyValuePair<string, Window> kv in _windows)
            {
                if (now - kv.Value.StartMs < _windowMs) continue;
                (expired ??= new List<string>()).Add(kv.Key);
            }
            if (expired == null) return;
            for (int i = 0; i < expired.Count; i++) _windows.Remove(expired[i]);
        }

        /// <summary>清扫间隔（毫秒）：到点或撞容量上限才全扫（摊销，见 <see cref="SweepLocked"/>）。</summary>
        private const long SweepIntervalMs = 1_000;
    }
}
