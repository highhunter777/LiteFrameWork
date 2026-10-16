using System;
using System.Collections.Generic;
using Xunit;

namespace LiteFramework.Tests
{
    [Collection("CoreStatic")]   // Log 环形缓冲 / ErrorCount 是全局静态
    public sealed class LogTests
    {
        [Fact]
        public void Log_写40条_Recent恰好32条_最旧被覆盖()
        {
            for (int i = 0; i < 40; i++) Log.Info($"msg-{i}");
            Assert.Equal(32, Log.Recent.Count);
            Assert.Equal("msg-39", Log.Recent[Log.Recent.Count - 1].Message);   // 最新在尾
            Assert.Equal("msg-8", Log.Recent[0].Message);                        // 前 8 条被覆盖
        }

        [Fact]
        public void Log_Error后_ErrorCount递增()
        {
            int before = Log.ErrorCount;
            Log.Error("boom");
            Log.Fatal("bang");
            Assert.Equal(before + 2, Log.ErrorCount);
        }

        [Fact]
        public void Log_带tag_Recent条目Tag与Message分离()
        {
            Log.Info("hello", LogTag.FileSys);
            var last = Log.Recent[Log.Recent.Count - 1];
            Assert.Equal(LogTag.FileSys, last.Tag);
            Assert.Equal("hello", last.Message);   // Message 不含前缀——HUD 自行格式化
        }

        [Fact]
        public void LogTag_词表穷尽_非None项均有显示名()
        {
            foreach (var tag in Enum.GetValues<LogTag>())
            {
                var name = Log.TagName(tag);       // 漏臂的枚举项走到这里即抛（switch 无 default 臂）
                if (tag == LogTag.None) Assert.Equal(string.Empty, name);
                else Assert.False(string.IsNullOrEmpty(name), $"LogTag.{tag} 缺显示名");
            }
        }

        [Fact]
        public void Log_输出前缀_非None带tag前缀_None无前缀()
        {
            var captured = new List<string>();
            Log.SetHelper(new CaptureHelper(captured));
            try
            {
                Log.Info("plain");
                Log.Info("tagged", LogTag.Scene);
                Log.Warning("warned", LogTag.Entity);
            }
            finally { Log.ResetForTesting(); }

            Assert.Equal(new[] { "plain", "[Scene] tagged", "[Entity] warned" }, captured);
        }

        private sealed class CaptureHelper : ILogHelper
        {
            private readonly List<string> _sink;
            public CaptureHelper(List<string> sink) => _sink = sink;
            public void Log(LogLevel level, string message) => _sink.Add(message);
        }
    }
}
