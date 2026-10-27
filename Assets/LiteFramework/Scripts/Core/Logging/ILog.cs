using System;
using System.Collections.Generic;

namespace LiteFramework
{
    /// <summary>
    /// 日志端口（B0）：等级门、写记录、作用域上下文。静态 <see cref="Log"/> 是兼容门面；
    /// 业务/适配器新代码注入本接口，基础设施静态工具继续走门面（Serilog/NLog 同款形态）。
    /// 实现须线程安全（环形缓冲写 + provider 回调；当前消费者主线程写、任意线程读）。
    /// </summary>
    public interface ILog
    {
        bool IsEnabled(LogLevel level);
        void Write(LogLevel level, LogTag tag, string message, Exception exception = null);
        IDisposable BeginScope(in LogScope scope);

        IReadOnlyList<LogEntry> Recent { get; }
        int ErrorCount { get; }
        string CurrentSessionId { get; }
        string CurrentMatchId { get; }
        int CurrentFrame { get; }
        int CurrentPlayerId { get; }

        void SetHelper(ILogHelper helper);
        void SetSink(ILogSink sink);
        void SetOutputLevel(LogLevel min);
        void SetMatchContext(string matchId, int playerId);
        void SetFrame(int frame);
        void ClearMatchContext(string matchId);
    }
}
