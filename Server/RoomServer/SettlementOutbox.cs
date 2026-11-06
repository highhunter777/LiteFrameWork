using System;
using System.Collections.Generic;
using System.Security;
using System.IO;
using System.Text;
using System.Text.Json;
using RoomServer.Runtime;

namespace RoomServer
{
    /// <summary>入盒结果（四态——与 Meta 侧持久化接缝同族语义：幂等/有界/失败全部显式）。</summary>
    public enum SettlementOutboxResult
    {
        /// <summary>已追加并落盘。</summary>
        Appended = 0,
        /// <summary>同 matchId 重复入盒——已存在，不重写（幂等：一局只冻结一次结算）。</summary>
        Duplicate = 1,
        /// <summary>达容量上限（按**待提交**数计）——显式拒绝（§6"有界 Outbox"）；不静默丢，计数归宿主。</summary>
        RejectedFull = 2,
        /// <summary>介质写失败——显性返回不抛（§6"持久化写入不得阻塞/炸掉 Room Worker"）。</summary>
        Failed = 3,
    }

    /// <summary>
    /// 待提交结算条目：冻结摘要 + **席位账号映射**（《上云测试专项设计》§4 裁决点① "结算载荷补 SeatAccountIds"——宿主在 SettlementReady 时从会话取得，随日志持久化。）。
    /// </summary>
    public sealed class PendingSettlement
    {
        public readonly MatchResultSummary Summary;
        public readonly string[] SeatAccountIds;

        public PendingSettlement(MatchResultSummary summary, string[] seatAccountIds)
        {
            Summary = summary ?? throw new ArgumentNullException(nameof(summary));
            SeatAccountIds = seatAccountIds == null ? Array.Empty<string>() : (string[])seatAccountIds.Clone();
        }

        /// <summary>幂等键（日志/标记/测试断言的便利投影）。</summary>
        public string MatchId
        {
            get { return Summary.MatchId; }
        }
    }

    /// <summary>
    /// 本地持久结算 Outbox 端口（《商业级通用服务端框架总设计》§7 钦定接口名 ISettlementOutbox。）。
    /// </summary>
    public interface ISettlementOutbox
    {
        /// <summary>入盒一条结算记录（幂等键 = matchId）。
        /// <paramref name="seatAccountIds"/> 与摘要 <c>Players</c> 同序等长（缺省 = 空，无账号归属）。</summary>
        SettlementOutboxResult Enqueue(MatchResultSummary summary, string[] seatAccountIds = null);

        /// <summary>
        /// 标记已完成（§11.3"Outbox 标记完成"——后台提交方拿到存储侧幂等确认后调用）。
        /// </summary>
        bool TryMarkCompleted(string matchId);

        /// <summary>
        /// 弃置（**终态处置**——确定性拒绝：载荷级 4xx，重投同一载荷不可能收敛；设计 §2"提交失败处置分级"）： 追加弃置标记行（<c>Kind="x"</c>。）。
        /// </summary>
        bool TryAbandon(string matchId, string reason);

        /// <summary>当前**待提交**条数（容量判据；标记完成后回落）。</summary>
        int Count { get; }

        /// <summary>待提交条目（重放/审计面；已完成条目不在其中）。</summary>
        IReadOnlyList<PendingSettlement> ListPending();

        /// <summary>§12 第 4 步"刷新 Outbox 到持久介质"（幂等；WriteThrough 形态下为收口确认）。
        /// 自上次压实后存在完成标记时顺带**压实日志**（重写为仅含待提交行）。</summary>
        void Flush();
    }

    /// <summary>
    /// 文件实现的本地持久 Outbox（**宿主层**——文件 IO 在此合法；Runtime/Application 保持零 IO）。
    /// </summary>
    public sealed class FileSettlementOutbox : ISettlementOutbox, IDisposable
    {
        /// <summary>自动压实阈值**缺省值**：连续完成标记达该数即重写日志（防判重集合随长跑无界增长）。
        /// 可配——宿主经 <see cref="HostTuning.OutboxCompactThresholdMarks"/> 注入（P2 批二）。</summary>
        public const int DefaultCompactThresholdMarks = 1024;

        private readonly int _compactThresholdMarks;

        private readonly string _journalPath;
        private readonly List<PendingSettlement> _entries;   // 待提交（容量判据）
        private readonly int _capacity;
        private HashSet<string> _seen;                        // 曾入盒的全部 matchId（含已完成；压实后收敛为待提交）
        private HashSet<string> _completed;                   // 已标记完成（压实后清空）
        private FileStream _stream;
        private int _marksSinceCompaction;
        private bool _disposed;

        /// <summary>
        /// 跨线程临界区（P0-1）：本类是**两条线程的汇合点**——入盒走 ServerLoop 线程 （<c>ServerHost.ApplyOutput</c> 内，60Hz 权威循环）。
        /// </summary>
        private readonly object _gate = new object();

        /// <summary>装载期跳过的坏行数（崩溃半行等；重启不炸——显式计数可观测）。</summary>
        public int SkippedCorruptLines { get; private set; }

        /// <summary>弃置条数（确定性拒绝终态处置；装载期自 "x" 标记行重建 + 运行时累加——诊断）。</summary>
        public int Abandoned { get; private set; }

        /// <summary>压实执行次数（诊断：长跑应随完成标记低频增长）。</summary>
        public int Compactions { get; private set; }

        /// <summary>压实失败次数（持续增长 = 介质异常；失败不改内存语义、下次收口重试）。</summary>
        public int CompactionFailures { get; private set; }

        private FileSettlementOutbox(string journalPath, FileStream stream, HashSet<string> seen,
            HashSet<string> completed, List<PendingSettlement> entries, int capacity, int compactThresholdMarks)
        {
            _journalPath = journalPath;
            _stream = stream;
            _seen = seen;
            _completed = completed;
            _entries = entries;
            _capacity = capacity;
            _compactThresholdMarks = compactThresholdMarks;
        }

        /// <summary>
        /// 打开（或续接）日志：目录不存在则建；既有内容装载入内存（坏行跳过计数，完成标记行合并进完成集），
        /// 新写入**追加**在既有内容之后——进程重启后既有待提交条目不丢（§12 第 4 步的恢复面）。
        /// </summary>
        public static FileSettlementOutbox Open(string path, int capacity,
            int compactThresholdMarks = DefaultCompactThresholdMarks)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("日志路径不能为空", nameof(path));
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Outbox 容量必须为正");
            if (compactThresholdMarks < 1)
                throw new ArgumentOutOfRangeException(nameof(compactThresholdMarks), compactThresholdMarks,
                    "压实阈值必须为正");

            string directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var completed = new HashSet<string>(StringComparer.Ordinal);
            var entries = new List<PendingSettlement>();
            int corrupt = 0;
            int abandoned = 0;
            if (File.Exists(path))
            {
                foreach (string line in File.ReadLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line)) { corrupt++; continue; }
                    JournalEntry entry;
                    try { entry = JsonSerializer.Deserialize<JournalEntry>(line); }
                    catch (JsonException) { corrupt++; continue; }
                    if (entry == null || string.IsNullOrEmpty(entry.MatchId)) { corrupt++; continue; }
                    if (entry.Players != null && Array.Exists(entry.Players, p => p == null)) { corrupt++; continue; }

                    if (entry.Kind == "c" || entry.Kind == "x")
                    {
                        // 完成/弃置标记行：退出待提交面（正常时序中随后于其结算行；
                        // 弃置 = 确定性拒绝终态——重放不得复活）
                        if (entry.Kind == "x") abandoned++;
                        completed.Add(entry.MatchId);
                        seen.Add(entry.MatchId);
                        entries.RemoveAll(e => e.MatchId == entry.MatchId);
                        continue;
                    }
                    if (!completed.Contains(entry.MatchId) && seen.Add(entry.MatchId))
                        entries.Add(ToPending(entry));              // 结算行（旧格式无 Kind/无账号同此）
                }
            }

            var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read,
                bufferSize: 4096, FileOptions.WriteThrough);
            return new FileSettlementOutbox(path, stream, seen, completed, entries, capacity, compactThresholdMarks)
            {
                SkippedCorruptLines = corrupt,
                Abandoned = abandoned,
            };
        }

        public int Count
        {
            get { lock (_gate) { return _entries.Count; } }
        }

        public SettlementOutboxResult Enqueue(MatchResultSummary summary, string[] seatAccountIds = null)
        {
            lock (_gate)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(FileSettlementOutbox));
                if (summary == null || string.IsNullOrEmpty(summary.MatchId)) return SettlementOutboxResult.Failed;
                if (_seen.Contains(summary.MatchId)) return SettlementOutboxResult.Duplicate;
                if (_entries.Count >= _capacity) return SettlementOutboxResult.RejectedFull;

                var pending = new PendingSettlement(summary, seatAccountIds);
                if (!TryAppend(ToEntry(pending)))
                {
                    return SettlementOutboxResult.Failed;
                }

                _seen.Add(summary.MatchId);
                _entries.Add(pending);
                return SettlementOutboxResult.Appended;
            }
        }

        /// <summary>
        /// 标记已完成：**标记行写先行**——崩溃窗口内"盘有标记、内存未及"自愈于重启重放；
        /// 写失败内存不动（可重试）。容量判据随之释放；达自动压实阈值即重写日志。
        /// </summary>
        public bool TryMarkCompleted(string matchId)
        {
            lock (_gate)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(FileSettlementOutbox));
                if (string.IsNullOrEmpty(matchId)) return false;
                if (!_seen.Contains(matchId) || _completed.Contains(matchId)) return false;
                int idx = _entries.FindIndex(e => e.MatchId == matchId);
                if (idx < 0) return false;                          // seen 与 entries 恒一致；防御性口径

                if (!TryAppend(new JournalEntry { Kind = "c", MatchId = matchId })) return false;

                _entries.RemoveAt(idx);
                _completed.Add(matchId);
                _marksSinceCompaction++;
                if (_marksSinceCompaction >= _compactThresholdMarks) Compact();
                return true;
            }
        }

        /// <summary>
        /// 弃置（确定性拒绝的终态处置；设计 §2"提交失败处置分级"）：**标记行写先行**——崩溃 自愈口径同完成标记（盘有标记、内存未及 → 重启重放收敛）；写失败内存不动（保持待提交， 可重试）。
        /// </summary>
        public bool TryAbandon(string matchId, string reason)
        {
            lock (_gate)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(FileSettlementOutbox));
                if (string.IsNullOrEmpty(matchId)) return false;
                if (!_seen.Contains(matchId) || _completed.Contains(matchId)) return false;
                int idx = _entries.FindIndex(e => e.MatchId == matchId);
                if (idx < 0) return false;

                if (!TryAppend(new JournalEntry { Kind = "x", MatchId = matchId, Reason = reason })) return false;

                _entries.RemoveAt(idx);
                _completed.Add(matchId);
                Abandoned++;
                _marksSinceCompaction++;
                if (_marksSinceCompaction >= _compactThresholdMarks) Compact();
                return true;
            }
        }

        public IReadOnlyList<PendingSettlement> ListPending()
        {
            lock (_gate) { return _entries.ToArray(); }   // 快照：调用方（后台提交线程）在锁外消费
        }

        public void Flush()
        {
            lock (_gate)
            {
                if (_disposed) return;
                if (_marksSinceCompaction > 0 && _stream.CanWrite) Compact();   // 收口即压实（有未压实标记时）
                if (_stream.CanWrite) _stream.Flush();
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                try { _stream.Flush(); } catch { /* 收尾失败不掩盖既有结果 */ }
                _stream.Dispose();
            }
        }

        /// <summary>追加一行并落盘（write-through）。失败返回 false，内存集合不动（与四态口径一致）。</summary>
        private bool TryAppend(JournalEntry entry)
        {
            try
            {
                byte[] line = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry));
                _stream.Write(line, 0, line.Length);
                _stream.WriteByte((byte)'\n');
                _stream.Flush();
                return true;
            }
            catch (Exception ex) when (ex is IOException
                                      || ex is UnauthorizedAccessException
                                      || ex is NotSupportedException
                                      || ex is SecurityException
                                      || ex is ObjectDisposedException)
            {
                // 介质故障：不抛进权威循环（§6）。内存集合未动——重试同 matchId 仍会尝试追加；
                // 半行残迹由装载期坏行跳过兜住。
                return false;
            }
        }

        /// <summary>
        /// 压实：日志重写为仅含**待提交**行（temp 写全 + File.Replace 原子替换），判重集合收敛为待提交。
        /// </summary>
        private void Compact()
        {
            string tempPath = _journalPath + ".compact";
            FileStream replaced = null;
            try
            {
                using (var temp = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None,
                    bufferSize: 4096, FileOptions.WriteThrough))
                {
                    foreach (PendingSettlement e in _entries)
                    {
                        byte[] line = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(ToEntry(e)));
                        temp.Write(line, 0, line.Length);
                        temp.WriteByte((byte)'\n');
                    }
                    temp.Flush();
                }

                replaced = _stream;
                replaced.Dispose();                              // File.Replace 要求目标无打开写句柄
                File.Replace(tempPath, _journalPath, null);
                _stream = new FileStream(_journalPath, FileMode.Append, FileAccess.Write, FileShare.Read,
                    bufferSize: 4096, FileOptions.WriteThrough);

                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (PendingSettlement e in _entries) seen.Add(e.MatchId);
                _seen = seen;
                _completed.Clear();
                _marksSinceCompaction = 0;
                Compactions++;
            }
            catch (Exception ex) when (ex is IOException
                                      || ex is UnauthorizedAccessException
                                      || ex is NotSupportedException
                                      || ex is SecurityException
                                      || ex is ObjectDisposedException)   // 与 TryAppend 同源：上次压实失败留下已关闭流时，重试路径不把异常抛进排空收尾链
            {
                CompactionFailures++;
                if (replaced != null && !_stream.CanWrite)
                {
                    // 原流已因替换流程关闭：复位为可续写的追加流；再失败则后续写路径显式 Failed
                    try
                    {
                        _stream = new FileStream(_journalPath, FileMode.Append, FileAccess.Write, FileShare.Read,
                            bufferSize: 4096, FileOptions.WriteThrough);
                    }
                    catch { /* 留下已关闭的流——写路径 catch(ObjectDisposed) 转 Failed */ }
                }
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* 半成品清理尽力而为 */ }
            }
        }

        // ---- 线格式（JSONL；字段名稳定——日志是可回放产物，不做版本协商前不改字段名。
        //      Kind 为追加字段：s=结算行 / c=完成标记行 / x=弃置标记行；缺省(null) = 结算行（既有日志兼容）；
        //      SeatAccountIds/Reason 同为追加字段：缺省(null) = 旧行无账号归属（提交时按空计）/无弃置原因。----

        private sealed class JournalEntry
        {
            public string Kind { get; set; }
            public string MatchId { get; set; }
            public long Seed { get; set; }
            public int FinalFrame { get; set; }
            public int EndReason { get; set; }
            public int[] SeatPlayerIds { get; set; }
            public long WinnerEntityId { get; set; }
            public int GameplayEndReason { get; set; }
            public PlayerEntry[] Players { get; set; }
            public string[] SeatAccountIds { get; set; }

            /// <summary>弃置原因（<c>Kind="x"</c> 标记行；追加字段——旧行缺省 null）。</summary>
            public string Reason { get; set; }
        }

        private sealed class PlayerEntry
        {
            public int PlayerId { get; set; }
            public long EntityId { get; set; }
            public int Kills { get; set; }
            public int Deaths { get; set; }
        }

        private static JournalEntry ToEntry(PendingSettlement pending)
        {
            MatchResultSummary s = pending.Summary;
            PlayerMatchResult[] results = s.Players;
            var players = new PlayerEntry[results.Length];
            for (int i = 0; i < players.Length; i++)
                players[i] = new PlayerEntry { PlayerId = results[i].PlayerId, EntityId = results[i].EntityId,
                    Kills = results[i].Kills, Deaths = results[i].Deaths };
            return new JournalEntry
            {
                Kind = "s", MatchId = s.MatchId, Seed = s.Seed, FinalFrame = s.FinalFrame,
                EndReason = (int)s.EndReason, SeatPlayerIds = s.SeatPlayerIds,
                WinnerEntityId = s.WinnerEntityId, GameplayEndReason = (int)s.GameplayEndReason, Players = players,
                SeatAccountIds = pending.SeatAccountIds,
            };
        }

        private static PendingSettlement ToPending(JournalEntry e)
        {
            return new PendingSettlement(ToSummary(e), e.SeatAccountIds);
        }

        private static MatchResultSummary ToSummary(JournalEntry e)
        {
            PlayerEntry[] source = e.Players ?? Array.Empty<PlayerEntry>();
            var players = new PlayerMatchResult[source.Length];
            for (int i = 0; i < players.Length; i++)
                players[i] = new PlayerMatchResult(source[i].PlayerId, source[i].EntityId, source[i].Kills, source[i].Deaths);
            return new MatchResultSummary(e.MatchId, e.Seed, e.FinalFrame, (ShutdownReason)e.EndReason,
                e.SeatPlayerIds ?? Array.Empty<int>(), e.WinnerEntityId, (LiteSim.MatchEndReason)e.GameplayEndReason, players);
        }
    }
}
