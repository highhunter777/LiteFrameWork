using System;
using System.IO;
using LiteClient;
using LiteFramework.Diagnostics;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 客户端现场包 Unity 侧接线：真实 Unity API（persistentDataPath/Application/Log）+ 写盘。
    /// BattleContext 的触发点由同一 helper 承担；本用例保证 helper 在 Unity 运行时可用。
    /// </summary>
    public sealed class ClientDiagnosticsBundleTests
    {
        [Test]
        public void Capture_WritesBundleContainingReason()
        {
            string dir = Path.Combine(Path.GetTempPath(), "client-diag-unity-" + Guid.NewGuid().ToString("N"));
            try
            {
                var writer = new DiagnosticsBundleWriter(dir, maxBundlesPerMatch: 3,
                    minInterval: TimeSpan.Zero, maxBytes: 1024 * 1024);

                string path = ClientDiagnosticsBundle.Capture(writer, "checksum-mismatch", "match-1",
                    frame: 42, localEntityId: 7, reconcileCount: 3, configDigest: 0x1234u);

                Assert.IsNotNull(path);
                Assert.IsTrue(File.Exists(Path.Combine(path, "manifest.json")));
                StringAssert.Contains("checksum-mismatch", File.ReadAllText(Path.Combine(path, "manifest.json")));
                Assert.IsTrue(File.Exists(Path.Combine(path, "log-recent.txt")));
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
