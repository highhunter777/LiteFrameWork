using System;
using System.Collections;
using System.Collections.Generic;


namespace LiteFramework
{
    public readonly struct LogEntry
    {
        public readonly LogLevel Level;
        // 本环境无 Environment.TickCount64(需 .NET Core 3.0+ / Unity 2021+),故用 TickCount。
        // 它是有符号 int,约 24.9 天(2^31 毫秒)即变负——环形缓冲只留最近 32 条且仅用于开发期显示,
        // 回绕/变负无实际影响。
        public readonly long Tick;       // Environment.TickCount(毫秒);显示层格式化,记录期零分配
        public readonly LogTag Tag;      // 词表见 LogTag(单源);None = 无。Message 不含 tag 前缀——HUD 自行格式化
        public readonly string Message;

        // ---- 关联上下文（T1 日志关联批）：记录时快照，零分配（字符串引用 + 两个 int）----
        public readonly string SessionId;   // 进程内会话标识（Log 启动时生成一次；空 = 未设）
        public readonly string MatchId;     // 对局标识（StartGame.MatchId；空 = 无对局）
        public readonly int Frame;          // 记录时的逻辑帧（0 = 未开局/未知）
        public readonly int PlayerId;       // 本地席位（-1 = 未知）

        public LogEntry(LogLevel level, long tick, LogTag tag, string message,
            string sessionId, string matchId, int frame, int playerId)
        {
            Level = level; Tick = tick; Tag = tag; Message = message;
            SessionId = sessionId; MatchId = matchId; Frame = frame; PlayerId = playerId;
        }
    }

    public static partial class Log
    {
        private const int Capacity = DiagnosticsDefaults.AuditRingCapacity;
        private static readonly LogEntry[] _ring = new LogEntry[Capacity];
        private static readonly object _ringGate = new object();   // 环形缓冲读写互斥（主线程写 + 任意线程回调读）
        private static int _start;                              // 最旧条目下标
        private static int _count;
        private static readonly RingView _recent = new();

        private static ILogHelper _helper;                      // null = 未注入,只丢输出不丢记录
        private static ILogSink _sink;                          // null = 无结构化出口（release/测试默认）

        // ---- 关联上下文（T1）：日志基础设施自身状态，非业务/判定状态；只读投影面消费 ----
        // 会话 id 进程内生成一次（不落盘、不进摘要）；对局/帧/席位由对局上下文在开局与每 tick 写入。
        // 写入都是 string 引用/int 赋值（零分配），读取方只读快照。
        private static readonly string _sessionId = Guid.NewGuid().ToString("N").Substring(0, 8);
        private static string _matchId = string.Empty;
        private static int _frame;
        private static int _playerId = -1;

        /// <summary>进程内会话标识（8 位；DevHUD/现场包用于把同一进程的日志串起来）。</summary>
        public static string CurrentSessionId => _sessionId;
        /// <summary>当前对局标识（空 = 无对局）。</summary>
        public static string CurrentMatchId => _matchId;
        /// <summary>当前逻辑帧（0 = 未开局/未知）。</summary>
        public static int CurrentFrame => _frame;
        /// <summary>本地席位（-1 = 未知）。</summary>
        public static int CurrentPlayerId => _playerId;

        /// <summary>登记对局上下文（StartGame 建点时调用；帧从 0 起）。</summary>
        public static void SetMatchContext(string matchId, int playerId)
        {
            _matchId = matchId ?? string.Empty;
            _playerId = playerId;
            _frame = 0;
        }

        /// <summary>推进逻辑帧（对局 Tick 每帧一次；int 赋值）。</summary>
        public static void SetFrame(int frame) => _frame = frame;

        /// <summary>清对局上下文（只清自己登记的那一局——防旧局晚拆清掉新局）。</summary>
        public static void ClearMatchContext(string matchId)
        {
            if (!string.Equals(_matchId, matchId)) return;
            _matchId = string.Empty;
            _frame = 0;
            _playerId = -1;
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
        private static LogLevel _minOutput = LogLevel.Info;      // 编辑器/开发构建：全量输出，运行时可用 SetOutputLevel 覆盖
#else
        private static LogLevel _minOutput = LogLevel.Warning;   // release：收紧，只出 Warning 以上
#endif

        public static int ErrorCount { get; private set; }
        public static IReadOnlyList<LogEntry> Recent => _recent; // index 0 = 最旧;零分配读

        public static void SetHelper(ILogHelper helper) => _helper = helper;
        /// <summary>注入/关闭结构化出口（实现须非阻塞；Log 侧对异常做防御性吞掉，不反噬业务）。</summary>
        public static void SetSink(ILogSink sink) => _sink = sink;
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
            _sink = null;
            ErrorCount = 0;
            Array.Clear(_ring, 0, _ring.Length);
            _start = 0;
            _count = 0;
            _matchId = string.Empty;
            _frame = 0;
            _playerId = -1;
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
            _minOutput = LogLevel.Info;
#else
            _minOutput = LogLevel.Warning;
#endif
        }

        // tag 契约:稳定词表见 LogTag 枚举(单源,新增 tag = 加枚举项);动态信息(实例名/路径)进 message
        // 不进 tag——Recent 只有 32 条,tag 集合必须小而稳定。
        public static void Info(string message, LogTag tag = LogTag.None) => Emit(LogLevel.Info, message, tag);
        public static void Warning(string message, LogTag tag = LogTag.None) => Emit(LogLevel.Warning, message, tag);
        public static void Error(string message, LogTag tag = LogTag.None) => Emit(LogLevel.Error, message, tag);
        public static void Error(Exception ex, string context = null, LogTag tag = LogTag.None)
            => Emit(LogLevel.Error,
                 ex == null ? "null exception"
                 : context == null ? ex.ToString() : $"{context}\n{ex}",
                 tag);
        public static void Fatal(string message, LogTag tag = LogTag.None) => Emit(LogLevel.Fatal, message, tag);
        public static void Fatal(Exception ex, string context = null, LogTag tag = LogTag.None)
            => Emit(LogLevel.Fatal,
                 ex == null ? "null exception"
                 : context == null ? ex.ToString() : $"{context}\n{ex}",
                 tag);

        private static void Emit(LogLevel level, string message, LogTag tag)
        {
            Record(level, tag, message);                 // 缓冲始终全记,与输出解耦
            if (level >= LogLevel.Error) ErrorCount++;   // Fatal 计入
            if (level < _minOutput || _helper == null) return;
            if (tag != LogTag.None) message = $"[{TagName(tag)}] {message}";   // helper 收统一前缀版(接口不变)
            try { _helper.Log(level, message); }
            catch { /* 输出器故障不反噬业务;catch 内不可再 Log(递归) */ }
        }

        private static void Record(LogLevel level, LogTag tag, string message)
        {
            LogEntry entry;
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
                    _sessionId, _matchId, _frame, _playerId);
                _ring[tail] = entry;
                _count++;
            }

            // 结构化出口：调用线程只做入队（实现契约），异常吞掉——日志设施故障不反噬业务。
            // 放在锁外：出口实现可能较慢/回调式，不能把任意代码带进环形缓冲的临界区。
            ILogSink sink = _sink;
            if (sink != null)
            {
                try { sink.Write(in entry); }
                catch { /* 同 ILogHelper：出口故障不影响记录/业务 */ }
            }
        }
        /// <summary>
        /// 最近日志的只读视图（**线程安全**：下标/计数逐次加锁；枚举取快照，迭代期间不撕裂）。
        /// 快照在枚举开始时分配一次——DevHUD/现场包低频读取，代价可接受。
        /// </summary>
        private sealed class RingView : IReadOnlyList<LogEntry>
        {
            public LogEntry this[int i]
            {
                get
                {
                    lock (_ringGate)
                    {
                        if (i < 0 || i >= _count) throw new ArgumentOutOfRangeException(nameof(i));
                        return _ring[(_start + i) % Capacity];
                    }
                }
            }

            public int Count { get { lock (_ringGate) return _count; } }

            public IEnumerator<LogEntry> GetEnumerator() => ((IEnumerable<LogEntry>)Snapshot()).GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            private static LogEntry[] Snapshot()
            {
                lock (_ringGate)
                {
                    var copy = new LogEntry[_count];
                    for (int i = 0; i < _count; i++) copy[i] = _ring[(_start + i) % Capacity];
                    return copy;
                }
            }
        }
    }
    }
