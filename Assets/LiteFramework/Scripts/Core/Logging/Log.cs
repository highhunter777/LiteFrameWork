using System;
using System.Collections;
using System.Collections.Generic;


namespace LiteFramework
{
    public readonly struct LogEntry
    {
        public readonly LogLevel Level;
        // 本环境无 Environment.TickCount64(需 .NET Core 3.0+ / Unity 2021+),故用 TickCount。
        // 它是 int,约 49.7 天回绕——环形缓冲只留最近 32 条且仅用于开发期显示,回绕无实际影响。
        public readonly long Tick;       // Environment.TickCount(毫秒);显示层格式化,记录期零分配
        public readonly string Tag;      // 模块名常量(如 "FileSys");null = 无。Message 不含 tag 前缀——HUD 自行格式化
        public readonly string Message;

        public LogEntry(LogLevel level, long tick, string tag, string message)
        {
            Level = level; Tick = tick; Tag = tag; Message = message;
        }
    }

    public static class Log
    {
        private const int Capacity = 32;
        private static readonly LogEntry[] _ring = new LogEntry[Capacity];
        private static int _start;                              // 最旧条目下标
        private static int _count;
        private static readonly RingView _recent = new();

        private static ILogHelper _helper;                      // null = 未注入,只丢输出不丢记录
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
        private static LogLevel _minOutput = LogLevel.Info;      // 编辑器/开发构建：全量输出，运行时可用 SetOutputLevel 覆盖
#else
        private static LogLevel _minOutput = LogLevel.Warning;   // release：收紧，只出 Warning 以上
#endif

        public static int ErrorCount { get; private set; }
        public static IReadOnlyList<LogEntry> Recent => _recent; // index 0 = 最旧;零分配读

        public static void SetHelper(ILogHelper helper) => _helper = helper;
        public static void SetOutputLevel(LogLevel min) => _minOutput = min;

        /// <summary>
        /// 复位全部静态状态（helper 注入、输出等级、环形缓冲、计数）。
        ///
        /// **为什么必须有**：<see cref="SetHelper"/> 注入的是**静态**字段，编辑器里跨测试运行存活。
        /// PlayMode 套件会跑完整引导链（<c>PlatformInfrastructureModule</c> 调
        /// <c>SetHelper(new UnityLogHelper())</c>），此后 Unity 的 <c>LogAssert</c> 就开始看见
        /// 本来被静默丢弃的错误日志——**故意触发错误日志的 EditMode 用例随即被判定
        /// "Unhandled log message"**。
        ///
        /// **谁该调**：测试夹具的 Setup/TearDown（保证用例在确定状态下跑），
        /// 以及编辑器域重载入口。生产代码**不要**调——注入的 helper 是进程级设施。
        /// </summary>
        public static void ResetForTesting()
        {
            _helper = null;
            ErrorCount = 0;
            Array.Clear(_ring, 0, _ring.Length);
            _start = 0;
            _count = 0;
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
            _minOutput = LogLevel.Info;
#else
            _minOutput = LogLevel.Warning;
#endif
        }

        // tag 契约:稳定的模块名常量("FileSys"/"Event");动态信息(实例名/路径)进 message 不进 tag——Recent 只有 32 条,tag 集合必须小而稳定。
        public static void Info(string message, string tag = null) => Emit(LogLevel.Info, message, tag);
        public static void Warning(string message, string tag = null) => Emit(LogLevel.Warning, message, tag);
        public static void Error(string message, string tag = null) => Emit(LogLevel.Error, message, tag);
        public static void Error(Exception ex, string context = null, string tag = null)
            => Emit(LogLevel.Error,
                 ex == null ? "null exception"
                 : context == null ? ex.ToString() : $"{context}\n{ex}",
                 tag);
        public static void Fatal(string message, string tag = null) => Emit(LogLevel.Fatal, message, tag);
        public static void Fatal(Exception ex, string context = null, string tag = null)
            => Emit(LogLevel.Fatal,
                 ex == null ? "null exception"
                 : context == null ? ex.ToString() : $"{context}\n{ex}",
                 tag);

        private static void Emit(LogLevel level, string message, string tag)
        {
            Record(level, tag, message);                 // 缓冲始终全记,与输出解耦
            if (level >= LogLevel.Error) ErrorCount++;   // Fatal 计入
            if (level < _minOutput || _helper == null) return;
            if (tag != null) message = $"[{tag}] {message}";   // helper 收统一前缀版(接口不变)
            try { _helper.Log(level, message); }
            catch { /* 输出器故障不反噬业务;catch 内不可再 Log(递归) */ }
        }

        private static void Record(LogLevel level, string tag, string message)
        {
            if (_count == Capacity)
            {
                _ring[_start] = default;                 // 放开滚出条目的 string 引用
                _start = (_start + 1) % Capacity;
                _count--;
            }
            int tail = (_start + _count) % Capacity;
            _ring[tail] = new LogEntry(level, Environment.TickCount, tag, message);
            _count++;
        }
        private sealed class RingView : IReadOnlyList<LogEntry>
        {
            public LogEntry this[int i] => i < 0 || i >= _count
                ? throw new ArgumentOutOfRangeException(nameof(i))
                : _ring[(_start + i) % Capacity];
            public int Count => _count;
            public Enumerator GetEnumerator() => new();
            IEnumerator<LogEntry> IEnumerable<LogEntry>.GetEnumerator() => GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            public struct Enumerator : IEnumerator<LogEntry>
            {
                private int _index;  // 0 = 未开始;Current 用 _index-1
                public LogEntry Current => Log._ring[(Log._start + _index - 1) % Capacity];
                object IEnumerator.Current => Current;
                public bool MoveNext() => _index < Log._count && ++_index <= Log._count;
                public void Reset() => _index = 0;
                public void Dispose() { }
            }
        }
    }
    }
