using System;
using System.Collections.Generic;
using LiteSim;
using RoomServer.Runtime;
using RoomServer.Runtime.Diagnostics;

namespace RoomServer.Diagnostics
{
    /// <summary>
    /// 服务端现场包编排：房间终态静默后取内核记录，补身份/日志尾/mismatch 摘要并落盘。
    /// 只做宿主层 IO；内核记录保持纯内存。任何失败都只告警，不影响房间收尾。
    /// </summary>
    public sealed class RoomDiagnosticsService
    {
        private sealed class MismatchState
        {
            public int Frame = -1;
            public long Reports;
            public DateTime LastAtUtc;
        }

        private readonly RoomRunStore _store;
        private readonly string _buildHash;
        private readonly object _mismatchGate = new object();
        private readonly Dictionary<string, MismatchState> _mismatches =
            new Dictionary<string, MismatchState>(StringComparer.Ordinal);

        public RoomDiagnosticsOptions Options { get; }

        public RoomDiagnosticsService(RoomDiagnosticsOptions options, string buildHash)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
            if (options.Enabled && string.IsNullOrWhiteSpace(buildHash))
                throw new ArgumentException("启用诊断时 buildHash 必填", nameof(buildHash));
            _buildHash = buildHash ?? string.Empty;
            _store = options.Enabled
                ? new RoomRunStore(options.Directory, options.MaxFiles, options.MaxTotalBytes)
                : null;
        }

        public void NoteMismatch(string roomId, int frame)
        {
            if (!Options.Enabled || string.IsNullOrEmpty(roomId)) return;
            lock (_mismatchGate)
            {
                if (!_mismatches.TryGetValue(roomId, out MismatchState state))
                {
                    state = new MismatchState();
                    _mismatches[roomId] = state;
                }
                state.Reports++;
                if (frame > state.Frame) state.Frame = frame;
                state.LastAtUtc = DateTime.UtcNow;
            }
        }

        public string TryWrite(string roomId, RoomRuntime runtime, RoomConfig config,
            uint gameplayDigestBase, DateTime startedAtUtc, string endReason)
        {
            if (!Options.Enabled || runtime == null || config == null) return null;

            try
            {
                RoomRunRecord record = runtime.TakeRunRecord();
                if (record == null)
                {
                    HostLog.Warn("Diag", $"房间 {roomId} 未启用录制，跳过现场包");
                    return null;
                }

                RoomRunMismatchDto mismatch = TakeMismatch(roomId);
                RoomRunEnvelope envelope = RoomRunEnvelope.Create(
                    record,
                    _buildHash,
                    runtime.FixedConfig.Digest,
                    gameplayDigestBase,
                    runtime.MapId,
                    SimMapDigest.Compute(runtime.Map),
                    RoomRunConfigDto.From(config),
                    startedAtUtc,
                    DateTime.UtcNow,
                    endReason,
                    HostLog.RecentTail(Options.LogTailLines),
                    mismatch,
                    RoomRunMapDto.From(runtime.Map));

                string path = _store.Save(envelope);
                if (path == null)
                {
                    HostLog.Warn("Diag", $"现场包写入失败 room={roomId}: {_store.LastError}");
                    return null;
                }
                HostLog.Info("Diag", $"现场包已落盘 room={roomId} steps={record.StepCount} file={path}");
                return path;
            }
            catch (Exception ex)
            {
                HostLog.Warn("Diag", $"现场包生成异常 room={roomId}: {ex.Message}");
                return null;
            }
        }

        private RoomRunMismatchDto TakeMismatch(string roomId)
        {
            lock (_mismatchGate)
            {
                if (string.IsNullOrEmpty(roomId) || !_mismatches.TryGetValue(roomId, out MismatchState state))
                    return null;
                _mismatches.Remove(roomId);
                return new RoomRunMismatchDto
                {
                    Frame = state.Frame,
                    Reports = state.Reports,
                    AtUtc = state.LastAtUtc,
                };
            }
        }
    }
}
