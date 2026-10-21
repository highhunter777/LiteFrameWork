using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using LiteSim;
using RoomServer.Runtime;
using RoomServer.Runtime.Diagnostics;

namespace RoomServer.Diagnostics
{
    /// <summary>GM 下载读面的文件摘要（诊断目录直下）。</summary>
    public sealed class DiagFileSummary
    {
        public string Name { get; set; }
        public long Bytes { get; set; }
        public DateTime LastWriteUtc { get; set; }
    }

    /// <summary>
    /// 服务端现场包编排：房间终态静默后取内核记录，补身份/日志尾/mismatch 摘要并落盘。
    /// 只做宿主层 IO；内核记录保持纯内存。任何失败都只告警，不影响房间收尾。
    ///
    /// mismatch 即时 flush（T0-2）：客户端和解上报到达时不再等房间终态——传输线程请求快照，
    /// 房间线程在下一 StepFrame 末封印独立记录（事件/世界环深拷）并入队，本服务的后台写线程
    /// 组装信封落盘（EndReason=mismatch-flush）。同房间冷却期内只落一份，终态记录不受影响。
    /// </summary>
    public sealed class RoomDiagnosticsService : IDisposable
    {
        /// <summary>同房间两次即时 flush 的最小间隔（突发多报只落一份现场，终态记录兜底）。</summary>
        private static readonly TimeSpan FlushCooldown = TimeSpan.FromSeconds(10);

        private sealed class MismatchState
        {
            public int Frame = -1;
            public long Reports;
            public DateTime LastAtUtc;
            public DateTime LastFlushUtc;
        }

        /// <summary>后台写线程的一个工作项：快照记录 + 该房间的不可变身份面。</summary>
        private sealed class FlushWorkItem
        {
            public RoomRunRecord Record;
            public string RoomId;
            public RoomRunConfigDto Config;
            public uint FixedConfigDigest;
            public uint GameplayDigestBase;
            public int MapId;
            public string MapDigest;
            public RoomRunMapDto Map;
            public DateTime StartedAtUtc;
        }

        private readonly RoomRunStore _store;
        private readonly string _buildHash;
        private readonly object _mismatchGate = new object();
        private readonly Dictionary<string, MismatchState> _mismatches =
            new Dictionary<string, MismatchState>(StringComparer.Ordinal);
        private readonly Queue<FlushWorkItem> _flushQueue = new Queue<FlushWorkItem>();
        private readonly AutoResetEvent _flushSignal;
        private readonly Thread _flushThread;
        private volatile bool _disposed;

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
            if (options.Enabled)
            {
                _flushSignal = new AutoResetEvent(false);
                _flushThread = new Thread(FlushLoop)
                {
                    IsBackground = true,
                    Name = "RoomServer.DiagFlush",
                };
                _flushThread.Start();
            }
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

        /// <summary>
        /// 是否应请求该房间的即时快照（冷却判定——同房间突发多报只落一份）。
        /// 返回 true 时已占用本次冷却窗口，调用方应立即在房间上置位快照请求。
        /// </summary>
        public bool ShouldRequestFlush(string roomId)
        {
            if (!Options.Enabled || string.IsNullOrEmpty(roomId)) return false;
            lock (_mismatchGate)
            {
                if (!_mismatches.TryGetValue(roomId, out MismatchState state)) return false;
                DateTime now = DateTime.UtcNow;
                if (now - state.LastFlushUtc < FlushCooldown) return false;
                state.LastFlushUtc = now;
                return true;
            }
        }

        /// <summary>
        /// mismatch 快照入队（房间线程回调里调用——只做入队，信封组装与写盘归后台线程）。
        /// 身份面（配置/地图/摘要）在此刻从只读字段捕获；房间线程上零 IO。
        /// </summary>
        public void EnqueueMismatchFlush(RoomRunRecord record, RoomRuntime runtime, RoomConfig config,
            uint gameplayDigestBase, DateTime startedAtUtc)
        {
            if (!Options.Enabled || _disposed || record == null || runtime == null || config == null) return;

            try
            {
                var item = new FlushWorkItem
                {
                    Record = record,
                    RoomId = record.RoomId,
                    Config = RoomRunConfigDto.From(config),
                    FixedConfigDigest = runtime.FixedConfig.Digest,
                    GameplayDigestBase = gameplayDigestBase,
                    MapId = runtime.MapId,
                    MapDigest = SimMapDigest.Compute(runtime.Map),
                    Map = RoomRunMapDto.From(runtime.Map),
                    StartedAtUtc = startedAtUtc,
                };
                lock (_flushQueue)
                {
                    _flushQueue.Enqueue(item);
                }
                _flushSignal.Set();
            }
            catch
            {
                // 入队失败只损失即时证据；终态记录照常兜底
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

        /// <summary>诊断目录内可下载的记录文件（GM 读面；不存在 = 空表）。</summary>
        public IReadOnlyList<string> ListFiles() => _store?.List() ?? Array.Empty<string>();

        /// <summary>GM 下载读面：文件清单（名/大小/修改时刻；仅诊断目录直下）。</summary>
        public IReadOnlyList<DiagFileSummary> ListFileSummaries()
        {
            IReadOnlyList<string> files = ListFiles();
            if (files.Count == 0) return Array.Empty<DiagFileSummary>();
            var summaries = new List<DiagFileSummary>(files.Count);
            for (int i = 0; i < files.Count; i++)
            {
                try
                {
                    var info = new FileInfo(files[i]);
                    summaries.Add(new DiagFileSummary
                    {
                        Name = info.Name,
                        Bytes = info.Length,
                        LastWriteUtc = info.LastWriteTimeUtc,
                    });
                }
                catch
                {
                    // 单文件消失（保留淘汰竞态）不阻断清单
                }
            }
            return summaries;
        }

        /// <summary>
        /// GM 下载读面：文件名 → 绝对路径（仅诊断目录直下、无分隔符/穿越；不存在或非法 = null）。
        /// 名字即能力边界——不接受任何相对路径形态。
        /// </summary>
        public string ResolveFile(string fileName)
        {
            if (_store == null || string.IsNullOrEmpty(fileName)) return null;
            if (fileName.IndexOf('/') >= 0 || fileName.IndexOf('\\') >= 0
                || fileName.Contains("..") || !string.Equals(fileName, System.IO.Path.GetFileName(fileName), StringComparison.Ordinal))
                return null;
            try
            {
                string path = System.IO.Path.Combine(_store.DirectoryPath, fileName);
                return File.Exists(path) ? path : null;
            }
            catch
            {
                return null;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _flushSignal?.Set();
            // 后台线程在 _disposed && 队列空时退出（剩余工作项尽力排干）
        }

        private void FlushLoop()
        {
            while (true)
            {
                FlushWorkItem item = null;
                lock (_flushQueue)
                {
                    if (_flushQueue.Count > 0) item = _flushQueue.Dequeue();
                }
                if (item == null)
                {
                    if (_disposed) return;
                    _flushSignal.WaitOne(1000);   // 超时兜底：Dispose 竞态下也能退出
                    continue;
                }
                try
                {
                    WriteFlush(item);
                }
                catch (Exception ex)
                {
                    HostLog.Warn("Diag", $"mismatch 现场包生成异常 room={item.RoomId}: {ex.Message}");
                }
            }
        }

        private void WriteFlush(FlushWorkItem item)
        {
            RoomRunMismatchDto mismatch = PeekMismatch(item.RoomId);
            RoomRunEnvelope envelope = RoomRunEnvelope.Create(
                item.Record,
                _buildHash,
                item.FixedConfigDigest,
                item.GameplayDigestBase,
                item.MapId,
                item.MapDigest,
                item.Config,
                item.StartedAtUtc,
                DateTime.UtcNow,
                "mismatch-flush",
                HostLog.RecentTail(Options.LogTailLines),
                mismatch,
                item.Map);

            string path = _store.Save(envelope);
            if (path == null)
            {
                HostLog.Warn("Diag", $"mismatch 现场包写入失败 room={item.RoomId}: {_store.LastError}");
                return;
            }
            HostLog.Info("Diag", $"mismatch 现场包已落盘 room={item.RoomId} steps={item.Record.StepCount} file={path}");
        }

        /// <summary>克隆 mismatch 摘要而不摘除（终态信封仍会带走完整计数）。</summary>
        private RoomRunMismatchDto PeekMismatch(string roomId)
        {
            lock (_mismatchGate)
            {
                if (string.IsNullOrEmpty(roomId) || !_mismatches.TryGetValue(roomId, out MismatchState state))
                    return null;
                return new RoomRunMismatchDto
                {
                    Frame = state.Frame,
                    Reports = state.Reports,
                    AtUtc = state.LastAtUtc,
                };
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
