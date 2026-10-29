using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LiteFramework;
using LiteFramework.Diagnostics;

namespace LiteFramework
{
    /// <summary>
    /// 客户端 mismatch 现场包的请求组装：从 Unity/Log 面取现场，写盘交给
    /// <see cref="DiagnosticsBundleWriter"/>。BattleContext 只负责触发；本类可独立测试。
    /// </summary>
    public static class ClientDiagnosticsBundle
    {
        /// <summary>
        /// Unity 托管异常捕获（T0-2）：Exception/Assert 级日志 → 客户端现场包（reason=unity-exception，
        /// 异常文案与堆栈尾进 ExtraJson）。其余级别（Log/Warning/Error）不捕获——Error 是可恢复面，
        /// 逐条落盘会被常规错误刷爆限频额度。同对局限频/限量由 writer 承担。
        /// </summary>
        public static string CaptureUnityException(DiagnosticsBundleWriter writer, string condition,
            string stackTrace, UnityEngine.LogType type, string matchId, int frame,
            long localEntityId, int reconcileCount, uint configDigest,
            DiagnosticIdentity? identity = null)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            if (type != UnityEngine.LogType.Exception && type != UnityEngine.LogType.Assert) return null;
            if (!writer.CanAccept(matchId)) return null;

            return Capture(writer, "unity-exception", matchId, frame, localEntityId, reconcileCount,
                configDigest, BuildExceptionExtra(condition, stackTrace), identity);
        }

        /// <summary>异常上下文进 ExtraJson（截尾防超长——完整堆栈在 Player.log 尾部件里）。</summary>
        private static string BuildExceptionExtra(string condition, string stackTrace)
        {
            const int budget = 2048;
            string text = (condition ?? string.Empty) + "\n" + (stackTrace ?? string.Empty);
            if (text.Length > budget) text = text.Substring(text.Length - budget);
            return text;
        }

        public static string Capture(DiagnosticsBundleWriter writer, string reason, string matchId, int frame,
            long localEntityId, int reconcileCount, uint configDigest, string extraJson = null,
            DiagnosticIdentity? identity = null)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            if (!writer.CanAccept(matchId)) return null;
            DiagnosticIdentity diag = identity ?? DiagnosticIdentity.Unknown;

            var logs = new List<string>(Log.Recent.Count);
            for (int i = 0; i < Log.Recent.Count; i++)
            {
                LogEntry e = Log.Recent[i];
                logs.Add(FormatLogLine(e));
            }

            return writer.Write(new DiagnosticsBundleRequest
            {
                Reason = reason,
                MatchId = matchId,
                RoomId = string.Empty,
                Frame = frame,
                LocalEntityId = localEntityId,
                ReconcileCount = reconcileCount,
                BuildHash = diag.BuildHash,
                ConfigDigest = configDigest,
                Platform = diag.Platform,
                AppVersion = diag.AppVersion,
                RecentLogs = logs,
                PlayerLogTail = ReadPlayerLogTail(64 * 1024),
                ExtraJson = extraJson,
            });
        }

        /// <summary>现场包日志行带关联上下文（与 DevHUD 同口径；未开局日志不附加）。</summary>
        private static string FormatLogLine(in LogEntry e)
            => e.MatchId.Length == 0 && e.Frame == 0 && e.PlayerId < 0
                ? $"{e.Level} [{e.Tag}] {e.Message}"
                : $"{e.Level} [{e.Tag}] {e.Message} [m:{e.MatchId} f:{e.Frame} p:{e.PlayerId}]";

        private static string ReadPlayerLogTail(int maxBytes)
        {
            try
            {
                string path = Path.Combine(UnityEngine.Application.persistentDataPath, "Player.log");
                if (!File.Exists(path)) return string.Empty;
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                long length = fs.Length;
                int take = (int)Math.Min(length, maxBytes);
                if (take <= 0) return string.Empty;
                fs.Seek(-take, SeekOrigin.End);
                var buffer = new byte[take];
                int read = fs.Read(buffer, 0, take);
                return read <= 0 ? string.Empty : Encoding.UTF8.GetString(buffer, 0, read);
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
