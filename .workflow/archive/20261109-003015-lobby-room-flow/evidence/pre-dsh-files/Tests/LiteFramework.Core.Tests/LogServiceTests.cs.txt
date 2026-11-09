using Xunit;

namespace LiteFramework.Tests
{
    /// <summary>日志实例（B0）契约：作用域上下文写入记录；静态门面安装实例后转发。</summary>
    [Collection("CoreStatic")]   // 安装/复位静态门面，与 LogTests 串行
    public sealed class LogServiceTests
    {
        [Fact]
        public void 作用域_覆盖会话对局帧与席位()
        {
            var log = new LogService(LogLevel.Info);   // 测试面 = 开发形态（默认发布级别 = Warning）
            using (log.BeginScope(new LogScope("session", "match", 42, 7)))
                log.Write(LogLevel.Info, LogTag.Battle, "hello");

            LogEntry entry = log.Recent[log.Recent.Count - 1];
            Assert.Equal("session", entry.SessionId);
            Assert.Equal("match", entry.MatchId);
            Assert.Equal(42, entry.Frame);
            Assert.Equal(7, entry.PlayerId);
            Assert.Equal("hello", entry.Message);
        }

        [Fact]
        public void 安装实例_静态门面转发到同一记录()
        {
            var log = new LogService(LogLevel.Info);
            try
            {
                Log.Install(log);
                Log.Warning("via-facade", LogTag.None);
                Assert.Contains("via-facade", log.Recent[log.Recent.Count - 1].Message);
            }
            finally
            {
                Log.ResetForTesting();
            }
        }
    }
}
