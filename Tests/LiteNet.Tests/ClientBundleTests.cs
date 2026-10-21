using System;
using System.IO;
using LiteFramework.Diagnostics;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 工具链 v2 第一刀：客户端 mismatch 现场包（纯逻辑，不依赖 Unity 运行时）。
    /// 锁的是“写出可用现场、同局限频、失败不反噬对局”。
    /// </summary>
    public sealed class ClientBundleTests
    {
        private static DiagnosticsBundleRequest Request() => new DiagnosticsBundleRequest
        {
            Reason = "checksum-mismatch",
            MatchId = "room:match",
            RoomId = "room",
            Frame = 42,
            LocalEntityId = 7,
            ReconcileCount = 3,
            BuildHash = "build-A",
            ConfigDigest = 0x1234u,
            Platform = "Windows",
            AppVersion = "test",
            RecentLogs = new[] { "log-a", "log-b" },
            PlayerLogTail = "player-tail",
            ExtraJson = "{\"note\":\"x\"}",
        };

        [Fact]
        public void 写出_mismatch现场包_含manifest与日志()
        {
            string dir = Path.Combine(Path.GetTempPath(), "client-diag-" + Guid.NewGuid().ToString("N"));
            try
            {
                var writer = new DiagnosticsBundleWriter(dir, maxBundlesPerMatch: 3,
                    minInterval: TimeSpan.Zero, maxBytes: 1024 * 1024);

                string path = writer.Write(Request());

                Assert.NotNull(path);
                Assert.True(File.Exists(Path.Combine(path, "manifest.json")));
                Assert.Contains("log-a", File.ReadAllText(Path.Combine(path, "log-recent.txt")));
                Assert.Contains("player-tail", File.ReadAllText(Path.Combine(path, "player-log-tail.txt")));
                Assert.Contains("checksum-mismatch", File.ReadAllText(Path.Combine(path, "manifest.json")));
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void 同局限频与保留上限_生效()
        {
            string dir = Path.Combine(Path.GetTempPath(), "client-diag-" + Guid.NewGuid().ToString("N"));
            try
            {
                var writer = new DiagnosticsBundleWriter(dir, maxBundlesPerMatch: 2,
                    minInterval: TimeSpan.Zero, maxBytes: 1024 * 1024);

                Assert.NotNull(writer.Write(Request()));
                Assert.NotNull(writer.Write(Request()));
                Assert.Null(writer.Write(Request()));
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
