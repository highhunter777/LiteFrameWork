using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;

namespace LiteFramework
{
    /// <summary>
    /// 日志实例实现（B0）：环形缓冲 + 输出 helper + 结构化 sink + 作用域上下文。
    /// 静态 <see cref="Log"/> 只是门面；组合根 Install 本实例后，静态调用全部转发到这里。
    /// 线程模型沿用原 Log：环形缓冲读写加锁，sink 在锁外调用（实现契约：非阻塞、不抛）。
    /// </summary>
    public sealed class LogService : ILog
    {
        private const int Capacity = DiagnosticsDefaults.AuditRingCapacity;
        private readonly LogEntry[] _ring = new LogEntry[Capacity];
        private readonly object _ringGate = new object();
        private readonly RingView _recent;
        private int _start;
        private int _count;

        private ILogHelper _helper;
        private ILogSink _sink;
        private LogLevel _minOutput;
        private readonly LogLevel _defaultMinOutput;   // 构造给定的最低级别（ResetForTesting 复位到此）
        private readonly string _sessionId = Guid.NewGuid().ToString("N").Substring(0, 8);
        private string _matchId = string.Empty;
        private int _frame;
        private int _playerId = -1;
        private readonly AsyncLocal<LogScope?> _scope = new AsyncLocal<LogScope?>();

        /// <param name="minOutput">最低输出级别：开发形态传 <see cref="LogLevel.Info"/>，发布形态保持默认
        /// <see cref="LogLevel.Warning"/>（装配点按 `DevBuildFacts.IsDevelopment` 传入；默认 = 历史 release 行为）。</param>
        public LogService(LogLevel minOutput = LogLevel.Warning)
        {
            _recent = new RingView(this);
            _minOutput = minOutput;
            _defaultMinOutput = minOutput;
        }

        public bool IsEnabled(LogLevel level) => level >= _minOutput;

        public IReadOnlyList<LogEntry> Recent => _recent;
        public int ErrorCount { get; private set; }

        public string CurrentSessionId =>
            string.IsNullOrEmpty(_scope.Value?.SessionId) ? _sessionId : _scope.Value.Value.SessionId;
        public string CurrentMatchId => _scope.Value?.MatchId ?? _matchId;
        public int CurrentFrame => _scope.Value?.Frame ?? _frame;
        public int CurrentPlayerId => _scope.Value?.PlayerId ?? _playerId;

        public void SetHelper(ILogHelper helper) => _helper = helper;
        public void SetSink(ILogSink sink) => _sink = sink;
        public void SetOutputLevel(LogLevel min) => _minOutput = min;

        public void SetMatchContext(string matchId, int playerId)
        {
            _matchId = matchId ?? string.Empty;
            _playerId = playerId;
            _frame = 0;
        }

        public void SetFrame(int frame) => _frame = frame;

        public void ClearMatchContext(string matchId)
        {
            if (!string.Equals(_matchId, matchId)) return;
            _matchId = string.Empty;
            _frame = 0;
            _playerId = -1;
        }

        public IDisposable BeginScope(in LogScope scope)
        {
            LogScope? previous = _scope.Value;
            _scope.Value = scope;
            return new Scope(this, previous);
        }

        public void Write(LogLevel level, LogTag tag, string message, Exception exception = null)
        {
            if (exception != null)
                message = string.IsNullOrEmpty(message) ? exception.ToString() : message + "\n" + exception;
            if (message == null) message = string.Empty;

            Record(level, tag, message);
            if (level >= LogLevel.Error) ErrorCount++;
            if (level < _minOutput || _helper == null) return;

            if (tag != LogTag.None) message = $"[{Log.TagName(tag)}] {message}";
            try { _helper.Log(level, message); }
            catch { /* 输出器故障不反噬业务；catch 内不可再 Log（递归） */ }
        }

        private void Record(LogLevel level, LogTag tag, string message)
        {
            LogEntry entry;
            LogScope? scope = _scope.Value;
            string session = string.IsNullOrEmpty(scope?.SessionId) ? _sessionId : scope.Value.SessionId;
            string match = scope?.MatchId ?? _matchId;
            int frame = scope?.Frame ?? _frame;
            int player = scope?.PlayerId ?? _playerId;

            lock (_ringGate)
            {
                if (_count == Capacity)
                {
                    _ring[_start] = default;                 // 放开滚出条目的 string 引用
                    _start = (_start + 1) % Capacity;
                    _count--;
                }
                int tail = (_start + _count) % Capacity;
                entry = new LogEntry(level, Environment.TickCount, tag, message,
                    session, match, frame, player);
                _ring[tail] = entry;
                _count++;
            }

            // 结构化出口：调用线程只做入队（实现契约），异常吞掉——日志设施故障不反噬业务。
            // 放在锁外：出口实现可能较慢/回调式，不能把任意代码带进环形缓冲的临界区。
            ILogSink sink = _sink;
            if (sink != null)
            {
                try { sink.Write(in entry); }
                catch { /* 同 helper：出口故障不影响记录/业务 */ }
            }
        }

        /// <summary>复位实例状态（测试/编辑器域重载用；生产不要调）。</summary>
        public void ResetForTesting()
        {
            _helper = null;
            _sink = null;
            ErrorCount = 0;
            lock (_ringGate)
            {
                Array.Clear(_ring, 0, _ring.Length);
                _start = 0;
                _count = 0;
            }
            _matchId = string.Empty;
            _frame = 0;
            _playerId = -1;
            _scope.Value = null;
            _minOutput = _defaultMinOutput;   // 复位到装配点给的级别（不再按编译宏猜）
        }

        private sealed class Scope : IDisposable
        {
            private LogService _owner;
            private readonly LogScope? _previous;

            public Scope(LogService owner, LogScope? previous)
            {
                _owner = owner;
                _previous = previous;
            }

            public void Dispose()
            {
                if (_owner == null) return;
                _owner._scope.Value = _previous;
                _owner = null;
            }
        }

        /// <summary>线程安全只读视图：下标/计数逐次加锁；枚举取快照，迭代期间不撕裂。</summary>
        private sealed class RingView : IReadOnlyList<LogEntry>
        {
            private readonly LogService _owner;

            public RingView(LogService owner) { _owner = owner; }

            public LogEntry this[int i]
            {
                get
                {
                    lock (_owner._ringGate)
                    {
                        if (i < 0 || i >= _owner._count) throw new ArgumentOutOfRangeException(nameof(i));
                        return _owner._ring[(_owner._start + i) % Capacity];
                    }
                }
            }

            public int Count { get { lock (_owner._ringGate) return _owner._count; } }

            public IEnumerator<LogEntry> GetEnumerator()
                => ((IEnumerable<LogEntry>)Snapshot()).GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            private LogEntry[] Snapshot()
            {
                lock (_owner._ringGate)
                {
                    var copy = new LogEntry[_owner._count];
                    for (int i = 0; i < _owner._count; i++)
                        copy[i] = _owner._ring[(_owner._start + i) % Capacity];
                    return copy;
                }
            }
        }
    }
}
