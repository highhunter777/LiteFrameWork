using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LiteFramework;
using LiteFramework.Diagnostics;
using LiteNet;

namespace LiteClient
{
    /// <summary>
    /// 客户端 mismatch 现场包的请求组装：从 Unity/Log 面取现场，写盘交给
    /// <see cref="DiagnosticsBundleWriter"/>。BattleContext 只负责触发；本类可独立测试。
    /// </summary>
    public static class ClientDiagnosticsBundle
    {
        public static string Capture(DiagnosticsBundleWriter writer, string reason, string matchId, int frame,
            long localEntityId, int reconcileCount, uint configDigest)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            if (!writer.CanAccept(matchId)) return null;

            var logs = new List<string>(Log.Recent.Count);
            for (int i = 0; i < Log.Recent.Count; i++)
            {
                LogEntry e = Log.Recent[i];
                logs.Add($"{e.Level} [{e.Tag}] {e.Message}");
            }

            return writer.Write(new DiagnosticsBundleRequest
            {
                Reason = reason,
                MatchId = matchId,
                RoomId = string.Empty,
                Frame = frame,
                LocalEntityId = localEntityId,
                ReconcileCount = reconcileCount,
                BuildHash = BuildHash.Value,
                ConfigDigest = configDigest,
                Platform = UnityEngine.Application.platform.ToString(),
                AppVersion = UnityEngine.Application.version,
                RecentLogs = logs,
                PlayerLogTail = ReadPlayerLogTail(64 * 1024),
            });
        }

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
