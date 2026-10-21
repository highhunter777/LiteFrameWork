using System;
using System.IO;
using System.Text.Json;
using RoomServer.Diagnostics;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>T0-2c：进程崩溃件——异常/堆栈/日志尾/buildHash 齐全且可解析；空异常与坏输入不抛。</summary>
    public sealed class CrashCaptureTests
    {
        [Fact]
        public void 崩溃件_异常字段齐全且可解析()
        {
            string dir = Path.Combine(Path.GetTempPath(), "crash-capture-" + Guid.NewGuid().ToString("N"));
            try
            {
                var inner = new ArgumentException("inner-cause");
                var exception = new InvalidOperationException("boom-message", inner);

                string path = CrashCapture.TryWrite(dir, "build-crash", logTailLines: 10,
                    exception, writeMinidump: false);

                Assert.NotNull(path);
                Assert.True(Path.GetFileName(path).StartsWith("crash-", StringComparison.Ordinal));
                Assert.True(path.EndsWith(".json", StringComparison.Ordinal));

                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
                JsonElement root = doc.RootElement;
                Assert.Equal("build-crash", root.GetProperty("buildHash").GetString());
                Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
                Assert.NotEmpty(root.GetProperty("capturedAtUtc").GetString());
                Assert.NotEmpty(root.GetProperty("runtime").GetString());

                JsonElement ex = root.GetProperty("exception");
                Assert.Contains("InvalidOperationException", ex.GetProperty("type").GetString());
                Assert.Equal("boom-message", ex.GetProperty("message").GetString());
                Assert.Equal("inner-cause", ex.GetProperty("inner").GetProperty("message").GetString());
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Fact]
        public void 崩溃件_空异常安全_目录自动创建()
        {
            string dir = Path.Combine(Path.GetTempPath(), "crash-capture-" + Guid.NewGuid().ToString("N"));
            try
            {
                string path = CrashCapture.TryWrite(dir, "b", 5, null, writeMinidump: false);

                Assert.NotNull(path);
                Assert.False(Directory.Exists(dir) && File.Exists(path + ".dmp"));
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
                // 空异常：exception 缺席（JsonIgnoreCondition 语义），其余身份面仍在
                Assert.False(doc.RootElement.TryGetProperty("exception", out _));
                Assert.Equal("b", doc.RootElement.GetProperty("buildHash").GetString());
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
