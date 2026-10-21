using System;
using System.IO;
using System.Linq;
using System.Text;
using LiteFramework.Diagnostics;
using Xunit;

namespace LiteFramework.Tests
{
    /// <summary>
    /// 结构化日志出口（T1）：JSONL 字段/转义/关联上下文、满队列丢最旧计数。
    /// 与 LogTests 共享静态面（Collection=CoreStatic），每例前后 ResetForTesting。
    /// </summary>
    [Collection("CoreStatic")]
    public sealed class StructuredLogSinkTests
    {
        [Fact]
        public void Sink_写出JSONL_带关联字段与转义()
        {
            string dir = TempDir();
            var sink = new StructuredLogSink(dir, capacity: 16, maxBytesPerFile: 1 << 20, maxFiles: 4);
            try
            {
                Log.ResetForTesting();
                sink.Start();
                Log.SetSink(sink);
                Log.SetMatchContext("m1", 3);
                Log.SetFrame(42);
                Log.Info("hello \"x\"\nline", LogTag.Battle);
                Assert.True(sink.Flush(TimeSpan.FromSeconds(5)), "flush 超时");

                string file = Directory.GetFiles(dir, "*.jsonl").Single();
                string line;
                using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                    line = reader.ReadToEnd().TrimEnd('\n');
                Assert.Contains("\"lvl\":\"Info\"", line);
                Assert.Contains("\"tag\":\"Battle\"", line);
                Assert.Contains("\"sid\":\"" + Log.CurrentSessionId + "\"", line);
                Assert.Contains("\"mid\":\"m1\"", line);
                Assert.Contains("\"f\":42", line);
                Assert.Contains("\"p\":3", line);
                Assert.Contains("hello \\\"x\\\"\\nline", line);
                Assert.DoesNotContain("\n", line);   // 消息内换行已转义，一行一条
            }
            finally
            {
                Log.SetSink(null);
                sink.Dispose();
                Log.ResetForTesting();
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void Sink_队列满_丢最旧并计数()
        {
            string dir = TempDir();
            var sink = new StructuredLogSink(dir, capacity: 4, maxBytesPerFile: 1 << 20, maxFiles: 2);
            try
            {
                Log.ResetForTesting();
                Log.SetSink(sink);
                for (int i = 0; i < 10; i++) Log.Info($"msg-{i}");
                Assert.Equal(6, sink.Dropped);
            }
            finally
            {
                Log.SetSink(null);
                sink.Dispose();
                Log.ResetForTesting();
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void Sink_单文件达到上限_轮转并只保留上限文件数()
        {
            string dir = TempDir();
            var sink = new StructuredLogSink(dir, capacity: 4096, maxBytesPerFile: 4096, maxFiles: 2);
            try
            {
                Log.ResetForTesting();
                sink.Start();
                Log.SetSink(sink);
                string payload = new string('x', 100);
                for (int i = 0; i < 200; i++) Log.Info(payload + i);
                Assert.True(sink.Flush(TimeSpan.FromSeconds(5)), "flush 超时");

                string[] files = Directory.GetFiles(dir, "*.jsonl");
                Assert.True(files.Length >= 2, "应至少轮转一次，实际=" + files.Length);
                Assert.True(files.Length <= 2, "只保留 maxFiles 件，实际=" + files.Length);
                Assert.Equal(0, sink.Dropped);
            }
            finally
            {
                Log.SetSink(null);
                sink.Dispose();
                Log.ResetForTesting();
                Directory.Delete(dir, true);
            }
        }

        private static string TempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "lf-log-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
