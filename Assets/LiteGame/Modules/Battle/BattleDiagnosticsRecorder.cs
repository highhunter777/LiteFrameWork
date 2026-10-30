using System;
using System.IO;
using LiteFramework;
using LiteFramework.Diagnostics;

namespace LiteGame
{
    /// <summary>
    /// 对局诊断现场包记录器：mismatch/配置身份不符/托管异常时落本地，正常路径不产生 IO。
    /// 限频与线程互斥在本类内，调用方只交事实（帧、身份、计数）。
    /// </summary>
    internal sealed class BattleDiagnosticsRecorder
    {
        private readonly object _gate = new object();
        private DiagnosticsBundleWriter _writer;
        private bool _rateLimited;   // 同局限频只提示一次（防 mismatch 刷屏 Warning）

        public void Capture(int frame, string reason, string matchId, long localEntityId,
            long reconcileCount, uint configDigest, DiagnosticIdentity identity)
        {
            try
            {
                lock (_gate)
                {
                    DiagnosticsBundleWriter writer = EnsureWriter();
                    if (!writer.CanAccept(matchId))
                    {
                        if (!_rateLimited)
                        {
                            _rateLimited = true;
                            Log.Info("诊断现场包同局限频，后续不再落包（每局上限由 writer 控制）", LogTag.Battle);
                        }
                        return;
                    }

                    string path = ClientDiagnosticsBundle.Capture(writer, reason, matchId, frame,
                        localEntityId, (int)Math.Min(reconcileCount, int.MaxValue), configDigest,
                        identity: identity);

                    if (path != null) Log.Info("诊断现场包已落盘: " + path, LogTag.Battle);
                    else Log.Warning("诊断现场包写出失败（IO/权限）", LogTag.Battle);
                }
            }
            catch (Exception ex)
            {
                Log.Warning("诊断现场包异常: " + ex.Message, LogTag.Battle);
            }
        }

        /// <summary>Unity 托管异常钩子（任意线程）：只捕获 Exception/Assert，过滤在 ClientDiagnosticsBundle。</summary>
        public void CaptureException(string condition, string stackTrace, UnityEngine.LogType type,
            string matchId, int frame, long localEntityId, long reconcileCount, uint configDigest,
            DiagnosticIdentity identity)
        {
            try
            {
                lock (_gate)
                {
                    DiagnosticsBundleWriter writer = EnsureWriter();
                    ClientDiagnosticsBundle.CaptureUnityException(writer, condition, stackTrace, type,
                        matchId, frame, localEntityId, (int)Math.Min(reconcileCount, int.MaxValue),
                        configDigest, identity);
                }
            }
            catch
            {
                // 诊断钩子不得把异常回灌 Unity 日志（递归触发自身）
            }
        }

        private DiagnosticsBundleWriter EnsureWriter()
        {
            DiagnosticsBundleWriter writer = _writer;
            if (writer == null)
            {
                writer = new DiagnosticsBundleWriter(Path.Combine(
                    UnityEngine.Application.persistentDataPath, "diagnostics"));
                _writer = writer;
            }
            return writer;
        }
    }
}
