using System;
using System.IO;
using LiteFramework;
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

        [Test]
        public void CaptureUnityException_异常级落包_其余级别不捕获()
        {
            string dir = Path.Combine(Path.GetTempPath(), "client-diag-unity-" + Guid.NewGuid().ToString("N"));
            try
            {
                var writer = new DiagnosticsBundleWriter(dir, maxBundlesPerMatch: 3,
                    minInterval: TimeSpan.Zero, maxBytes: 1024 * 1024);

                // Exception 级：落包，reason=unity-exception，异常上下文进 extra.txt
                string path = ClientDiagnosticsBundle.CaptureUnityException(writer,
                    "NullReferenceException: Object reference not set",
                    "LiteGame.Foo.Bar ()", UnityEngine.LogType.Exception,
                    "match-exc", frame: 9, localEntityId: 1, reconcileCount: 0, configDigest: 0u);
                Assert.IsNotNull(path);
                Assert.IsTrue(File.Exists(Path.Combine(path, "manifest.json")));
                StringAssert.Contains("unity-exception", File.ReadAllText(Path.Combine(path, "manifest.json")));
                StringAssert.Contains("extra.txt", File.ReadAllText(Path.Combine(path, "manifest.json")));
                StringAssert.Contains("NullReferenceException",
                    File.ReadAllText(Path.Combine(path, "extra.txt")));

                // Assert 级：同样落包
                string assertPath = ClientDiagnosticsBundle.CaptureUnityException(writer,
                    "assertion failed", string.Empty, UnityEngine.LogType.Assert,
                    "match-exc2", frame: 9, localEntityId: 1, reconcileCount: 0, configDigest: 0u);
                Assert.IsNotNull(assertPath);

                // Log/Warning/Error：可恢复面，不捕获（防常规错误刷爆限频额度）
                Assert.IsNull(ClientDiagnosticsBundle.CaptureUnityException(writer, "info", string.Empty,
                    UnityEngine.LogType.Log, "match-exc3", 1, 1, 0, 0u));
                Assert.IsNull(ClientDiagnosticsBundle.CaptureUnityException(writer, "warn", string.Empty,
                    UnityEngine.LogType.Warning, "match-exc3", 1, 1, 0, 0u));
                Assert.IsNull(ClientDiagnosticsBundle.CaptureUnityException(writer, "error", string.Empty,
                    UnityEngine.LogType.Error, "match-exc3", 1, 1, 0, 0u));
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
