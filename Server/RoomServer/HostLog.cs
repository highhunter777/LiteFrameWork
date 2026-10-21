using System;
using System.Text;

namespace RoomServer
{
    /// <summary>
    /// 宿主日志口（P4"结构化日志层"）：把宿主逻辑里的 <c>Console.WriteLine</c> 直落收成一条窄接缝。
    ///
    /// **为什么要收**（不是"好不好看"）：
    /// - 逻辑层绑死控制台——嵌入式宿主/测试里无处接管，也就**断言不了"该报的报了"**
    ///   （没人能证明 Ops 行、排空行、拒绝行真的输出过）；
    /// - 无**级别**与**区域**：运维读日志分不出"正常信息 / 显式拒绝与降级 / 已吸收的错误"，
    ///   只能靠前缀字符串猜（而前缀还在各文件里各写各的）；
    /// - 一改输出形态就得翻遍 20+ 处字面量。
    ///
    /// **形状**：<c>Info/Warn/Error(area, message)</c> + 可替换 <see cref="Sink"/>。
    /// 缺省落 Console（<c>[级别][区域] 文本</c>）；测试/嵌入式宿主接管 Sink 即把日志变成断言面。
    ///
    /// **不引日志框架**：本层只需要"级别 + 区域 + 一行文本"。结构化字段（traceId/房间号标签）、
    /// 采样、异步落盘归 R4 的 OpenTelemetry 接线（服务端总设计 §11.2 明列），此处不预置半个框架。
    ///
    /// **边界**：只管**宿主逻辑**的输出。`Program` 的 CLI/启动用法打印（用法提示、参数错误）
    /// 仍是控制台程序自身的 UI，不走此处——那不是服务日志。
    /// </summary>
    public static class HostLog
    {
        /// <summary>日志级别（三档够用：正常信息 / 显式拒绝与降级 / 已吸收的错误）。</summary>
        public enum Level
        {
            Info = 0,
            Warn = 1,
            Error = 2,
        }

        /// <summary>
        /// 日志出口（<c>(级别, 区域, 文本)</c>）。缺省 null = 落 Console；
        /// 测试/嵌入式宿主可接管（例如收进列表，断言"该报的报了/不该报的没报"）。
        /// </summary>
        public static Action<Level, string, string> Sink;

        private const int RecentCapacity = 256;
        private static readonly string[] RecentLines = new string[RecentCapacity];
        private static readonly object RecentGate = new object();
        private static int _recentStart;
        private static int _recentCount;

        public static void Info(string area, string message) => Emit(Level.Info, area, message);

        public static void Warn(string area, string message) => Emit(Level.Warn, area, message);

        public static void Error(string area, string message) => Emit(Level.Error, area, message);

        /// <summary>最近日志尾（现场包用；按写入顺序，最多 maxLines 行）。</summary>
        public static string RecentTail(int maxLines)
        {
            if (maxLines <= 0) return string.Empty;
            lock (RecentGate)
            {
                int count = Math.Min(maxLines, _recentCount);
                var sb = new StringBuilder();
                for (int i = _recentCount - count; i < _recentCount; i++)
                {
                    int index = (_recentStart + i) % RecentCapacity;
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(RecentLines[index] ?? string.Empty);
                }
                return sb.ToString();
            }
        }

        private static void Emit(Level level, string area, string message)
        {
            string line = "[" + LevelName(level) + "][" + (area ?? "-") + "] " + message;
            lock (RecentGate)
            {
                int index = (_recentStart + _recentCount) % RecentCapacity;
                RecentLines[index] = line;
                if (_recentCount < RecentCapacity) _recentCount++;
                else _recentStart = (_recentStart + 1) % RecentCapacity;
            }

            Action<Level, string, string> sink = Sink;
            if (sink != null)
            {
                sink(level, area, message);
                return;
            }
            Console.WriteLine(line);
        }

        private static string LevelName(Level level)
        {
            switch (level)
            {
                case Level.Warn: return "WARN";
                case Level.Error: return "ERROR";
                default: return "INFO";
            }
        }
    }
}
