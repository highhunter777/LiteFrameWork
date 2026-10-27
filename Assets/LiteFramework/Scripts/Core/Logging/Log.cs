using System;
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

    /// <summary>
    /// 日志**兼容门面**（Serilog/NLog 式静态 host）：真实 owner 是 <see cref="ILog"/>/<see cref="LogService"/>。
    /// 组合根调用 <see cref="Install"/> 安装实例；业务新代码应注入 <see cref="ILog"/>，静态工具/Lua/旧代码
    /// 继续走本门面。B3 再按程序集 allowlist 收敛。
    /// </summary>
    public static partial class Log
    {
        private static ILog s_log = new LogService();

        /// <summary>当前实例（诊断/测试可直接读；业务不要拿它做服务定位）。</summary>
        public static ILog Current => s_log;

        /// <summary>装配期安装实例（组合根调用一次）；null = 恢复默认实例。</summary>
        public static void Install(ILog log) => s_log = log ?? new LogService();

        public static bool IsEnabled(LogLevel level) => s_log.IsEnabled(level);

        public static IReadOnlyList<LogEntry> Recent => s_log.Recent;
        public static int ErrorCount => s_log.ErrorCount;
        public static string CurrentSessionId => s_log.CurrentSessionId;
        public static string CurrentMatchId => s_log.CurrentMatchId;
        public static int CurrentFrame => s_log.CurrentFrame;
        public static int CurrentPlayerId => s_log.CurrentPlayerId;

        public static void SetHelper(ILogHelper helper) => s_log.SetHelper(helper);
        /// <summary>注入/关闭结构化出口（实现须非阻塞；Log 侧对异常做防御性吞掉，不反噬业务）。</summary>
        public static void SetSink(ILogSink sink) => s_log.SetSink(sink);
        public static void SetOutputLevel(LogLevel min) => s_log.SetOutputLevel(min);

        /// <summary>登记对局上下文（StartGame 建点时调用；帧从 0 起）。</summary>
        public static void SetMatchContext(string matchId, int playerId)
            => s_log.SetMatchContext(matchId, playerId);

        /// <summary>推进逻辑帧（对局 Tick 每帧一次；int 赋值）。</summary>
        public static void SetFrame(int frame) => s_log.SetFrame(frame);

        /// <summary>清对局上下文（只清自己登记的那一局——防旧局晚拆清掉新局）。</summary>
        public static void ClearMatchContext(string matchId) => s_log.ClearMatchContext(matchId);

        /// <summary>压入关联上下文（替代散落的 SetMatchContext/SetFrame；using 作用域退出即恢复）。</summary>
        public static IDisposable BeginScope(in LogScope scope) => s_log.BeginScope(scope);

        // tag 契约:稳定词表见 LogTag 枚举(单源,新增 tag = 加枚举项);动态信息(实例名/路径)进 message
        // 不进 tag——Recent 只有 32 条,tag 集合必须小而稳定。
        public static void Info(string message, LogTag tag = LogTag.None)
            => s_log.Write(LogLevel.Info, tag, message);
        public static void Warning(string message, LogTag tag = LogTag.None)
            => s_log.Write(LogLevel.Warning, tag, message);
        public static void Error(string message, LogTag tag = LogTag.None)
            => s_log.Write(LogLevel.Error, tag, message);
        public static void Error(Exception ex, string context = null, LogTag tag = LogTag.None)
            => s_log.Write(LogLevel.Error, tag, context, ex);
        public static void Fatal(string message, LogTag tag = LogTag.None)
            => s_log.Write(LogLevel.Fatal, tag, message);
        public static void Fatal(Exception ex, string context = null, LogTag tag = LogTag.None)
            => s_log.Write(LogLevel.Fatal, tag, context, ex);

        /// <summary>
        /// 复位门面到全新实例（测试/编辑器域重载用；生产不要调）。
        /// 为什么必须有：helper/sink 是进程级设施，PlayMode 引导链注入后会跨用例存活。
        /// </summary>
        public static void ResetForTesting() => s_log = new LogService();
    }
}
