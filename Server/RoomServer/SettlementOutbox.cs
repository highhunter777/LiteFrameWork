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
    /// 本地持久结算 Outbox 端口（《商业级通用服务端框架总设计》§7 钦定接口名 ISettlementOutbox、
    /// §11.3"RoomRuntime 冻结 MatchResult → … → **本地持久 Outbox** → 后台提交 Profile.Apply →
    /// **Outbox 标记完成**"、§6"需要持久化的结果先写入**有界** Outbox"）。
    ///
    /// 载荷为 <see cref="MatchResultSummary"/>（SettlementReadyOutput）。
    ///
    /// **同步签名**对应宿主单循环形态（§10.2——命令直投、ApplyOutput 在 Pump 线程上）；
    /// 入盒/完成标记都是低频事件（一局至多一次、一行一条）。
    /// </summary>
    public interface ISettlementOutbox
    {
        /// <summary>入盒一条结算记录（幂等键 = matchId）。</summary>
        SettlementOutboxResult Enqueue(MatchResultSummary summary);

        /// <summary>
        /// 标记已完成（§11.3"Outbox 标记完成"——后台提交方拿到存储侧幂等确认后调用）。
        /// 容量按**待提交**数计：标记即释放容量、条目退出 <see cref="ListPending"/>。
        /// 未知 matchId / 已完成 / 介质写失败 → false（幂等，可重试）。
        /// </summary>
        bool TryMarkCompleted(string matchId);

        /// <summary>当前**待提交**条数（容量判据；标记完成后回落）。</summary>
        int Count { get; }

        /// <summary>待提交条目（重放/审计面；已完成条目不在其中）。</summary>
        IReadOnlyList<MatchResultSummary> ListPending();

        /// <summary>§12 第 4 步"刷新 Outbox 到持久介质"（幂等；WriteThrough 形态下为收口确认）。
        /// 自上次压实后存在完成标记时顺带**压实日志**（重写为仅含待提交行）。</summary>
        void Flush();
    }

    /// <summary>
    /// 文件实现的本地持久 Outbox（**宿主层**——文件 IO 在此合法；Runtime/Application 保持零 IO）。
    ///
    /// - **JSONL 日志**：一行一条（DTO 序列化；MatchResultSummary 是只读字段，经内部 DTO 转换）；
    /// - **逐条 write-through**：`FileOptions.WriteThrough` + 每条 Flush——Enqueue/标记返回时该行已在介质上；
    ///   崩溃窗口最多留下半行，装载期跳过并计数（SettlementReady 是"冻结待提交"事实）；
    /// - **幂等**：matchId 内存集合判重（装载期从日志重建）——重复入盒返回 Duplicate 不重写；
    ///   **完成语义**：`TryMarkCompleted` 追加完成标记行并退出待提交面；压实会把已完成行从日志移除，
    ///   判重集合随之收敛为待提交——此后同 matchId 再入盒会作为新待提交接受，
    ///   "重复提交不重复发奖"由 Meta 台账唯一索引兜底（§11.2 唯一索引 = 最终幂等裁判）；
    /// - **有界**：容量上限（配置）按**待提交**数计——标记即释放，长跑不因已完成条数涨满；
    ///   内存判重集合随**压实**收敛（自动：连续标记达 <see cref="CompactThresholdMarks"/>；
    ///   收口：`Flush` 有未压实标记时）——长跑内存不无界增长；
    /// - **路径红线**：必须 <c>.journal</c> 后缀（<see cref="RoomServerConfig"/> 装载期强制）——
    ///   RoomServer/Data 的 .json 参与 buildHash 哈希闭包，日志若落在那里会随对局漂移、
    ///   两端握手全拒。缺省路径 Outbox/settlements.journal（.gitignore 已登记）。
    /// </summary>
    public sealed class FileSettlementOutbox : ISettlementOutbox, IDisposable
    {
        /// <summary>自动压实阈值：连续完成标记达该数即重写日志（防判重集合随长跑无界增长）。</summary>
        private const int CompactThresholdMarks = 1024;

        private readonly string _journalPath;
        private readonly List<MatchResultSummary> _entries;   // 待提交（容量判据）
        private readonly int _capacity;
        private HashSet<string> _seen;                        // 曾入盒的全部 matchId（含已完成；压实后收敛为待提交）
        private HashSet<string> _completed;                   // 已标记完成（压实后清空）
        private FileStream _stream;
        private int _marksSinceCompaction;
        private bool _disposed;

        /// <summary>装载期跳过的坏行数（崩溃半行等；重启不炸——显式计数可观测）。</summary>
        public int SkippedCorruptLines { get; private set; }

        /// <summary>压实执行次数（诊断：长跑应随完成标记低频增长）。</summary>
        public int Compactions { get; private set; }

        /// <summary>压实失败次数（持续增长 = 介质异常；失败不改内存语义、下次收口重试）。</summary>
        public int CompactionFailures { get; private set; }

        private FileSettlementOutbox(string journalPath, FileStream stream, HashSet<string> seen,
            HashSet<string> completed, List<MatchResultSummary> entries, int capacity)
        {
            _journalPath = journalPath;
            _stream = stream;
            _seen = seen;
            _completed = completed;
            _entries = entries;
            _capacity = capacity;
        }

        /// <summary>
        /// 打开（或续接）日志：目录不存在则建；既有内容装载入内存（坏行跳过计数，完成标记行合并进完成集），
        /// 新写入**追加**在既有内容之后——进程重启后既有待提交条目不丢（§12 第 4 步的恢复面）。
        /// </summary>
        public static FileSettlementOutbox Open(string path, int capacity)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("日志路径不能为空", nameof(path));
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Outbox 容量必须为正");

            string directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var completed = new HashSet<string>(StringComparer.Ordinal);
            var entries = new List<MatchResultSummary>();
            int corrupt = 0;
            if (File.Exists(path))
            {
                foreach (string line in File.ReadLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line)) { corrupt++; continue; }
                    JournalEntry entry;
                    try { entry = JsonSerializer.Deserialize<JournalEntry>(line); }
                    catch (JsonException) { corrupt++; continue; }
                    if (entry == null || string.IsNullOrEmpty(entry.MatchId)) { corrupt++; continue; }

                    if (entry.Kind == "c")
                    {
                        // 完成标记行：退出待提交面（正常时序中随后于其结算行）
                        completed.Add(entry.MatchId);
                        seen.Add(entry.MatchId);
                        entries.RemoveAll(e => e.MatchId == entry.MatchId);
                        continue;
                    }
                    if (!completed.Contains(entry.MatchId) && seen.Add(entry.MatchId))
                        entries.Add(ToSummary(entry));              // 结算行（旧格式无 Kind 同此）
                }
            }

            var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read,
                bufferSize: 4096, FileOptions.WriteThrough);
            return new FileSettlementOutbox(path, stream, seen, completed, entries, capacity)
            {
                SkippedCorruptLines = corrupt,
            };
        }

        public int Count
        {
            get { return _entries.Count; }
        }

        public SettlementOutboxResult Enqueue(MatchResultSummary summary)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(FileSettlementOutbox));
            if (summary == null || string.IsNullOrEmpty(summary.MatchId)) return SettlementOutboxResult.Failed;
            if (_seen.Contains(summary.MatchId)) return SettlementOutboxResult.Duplicate;
            if (_entries.Count >= _capacity) return SettlementOutboxResult.RejectedFull;

            if (!TryAppend(new JournalEntry { Kind = "s", MatchId = summary.MatchId, Seed = summary.Seed,
                FinalFrame = summary.FinalFrame, EndReason = (int)summary.EndReason,
                SeatPlayerIds = summary.SeatPlayerIds }))
            {
                return SettlementOutboxResult.Failed;
            }

            _seen.Add(summary.MatchId);
            _entries.Add(summary);
            return SettlementOutboxResult.Appended;
        }

        /// <summary>
        /// 标记已完成：**标记行写先行**——崩溃窗口内"盘有标记、内存未及"自愈于重启重放；
        /// 写失败内存不动（可重试）。容量判据随之释放；达自动压实阈值即重写日志。
        /// </summary>
        public bool TryMarkCompleted(string matchId)
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
            if (_marksSinceCompaction >= CompactThresholdMarks) Compact();
            return true;
        }

        public IReadOnlyList<MatchResultSummary> ListPending()
        {
            return _entries.ToArray();
        }

        public void Flush()
        {
            if (_disposed) return;
            if (_marksSinceCompaction > 0 && _stream.CanWrite) Compact();   // 收口即压实（有未压实标记时）
            if (_stream.CanWrite) _stream.Flush();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _stream.Flush(); } catch { /* 收尾失败不掩盖既有结果 */ }
            _stream.Dispose();
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
        /// 已完成行被移除的安全语义：完成 = 提交方已拿到存储侧幂等确认，此后即便同 matchId 重入盒重提交，
        /// Meta 台账唯一索引兜底返回首次结果（§11.2）。介质故障路径：不抛、计 <see cref="CompactionFailures"/>，
        /// 内存语义不变、下次收口重试；日志保持追加原样（最坏回退 = 不压实，不改正确性）。
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
                    foreach (MatchResultSummary e in _entries)
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
                foreach (MatchResultSummary e in _entries) seen.Add(e.MatchId);
                _seen = seen;
                _completed.Clear();
                _marksSinceCompaction = 0;
                Compactions++;
            }
            catch (Exception ex) when (ex is IOException
                                      || ex is UnauthorizedAccessException
                                      || ex is NotSupportedException
                                      || ex is SecurityException)
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
        //      Kind 为追加字段：s=结算行 / c=完成标记行；缺省(null) = 结算行（既有日志兼容））----

        private sealed class JournalEntry
        {
            public string Kind { get; set; }
            public string MatchId { get; set; }
            public long Seed { get; set; }
            public int FinalFrame { get; set; }
            public int EndReason { get; set; }
            public int[] SeatPlayerIds { get; set; }
        }

        private static JournalEntry ToEntry(MatchResultSummary s) => new JournalEntry
        {
            Kind = "s",
            MatchId = s.MatchId,
            Seed = s.Seed,
            FinalFrame = s.FinalFrame,
            EndReason = (int)s.EndReason,
            SeatPlayerIds = s.SeatPlayerIds,
        };

        private static MatchResultSummary ToSummary(JournalEntry e) => new MatchResultSummary(
            e.MatchId, e.Seed, e.FinalFrame, (ShutdownReason)e.EndReason, e.SeatPlayerIds ?? Array.Empty<int>());
    }
}
