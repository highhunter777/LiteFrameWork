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
        /// <summary>达容量上限——显式拒绝（§6"有界 Outbox"）；不静默丢，计数归宿主。</summary>
        RejectedFull = 2,
        /// <summary>介质写失败——显性返回不抛（§6"持久化写入不得阻塞/炸掉 Room Worker"）。</summary>
        Failed = 3,
    }

    /// <summary>
    /// 本地持久结算 Outbox 端口（《商业级通用服务端框架总设计》§7 钦定接口名 ISettlementOutbox、
    /// §11.3"RoomRuntime 冻结 MatchResult → … → **本地持久 Outbox** → 后台提交 Profile.Apply"、
    /// §6"需要持久化的结果先写入**有界** Outbox"）。
    ///
    /// 载荷为 <see cref="MatchResultSummary"/>（SettlementReadyOutput）。
    ///
    /// **同步签名**对应宿主单循环形态（§10.2——命令直投、ApplyOutput 在 Pump 线程上）；
    /// 入盒是低频事件（一局一次、一行一条）。
    /// </summary>
    public interface ISettlementOutbox
    {
        /// <summary>入盒一条结算记录（幂等键 = matchId）。</summary>
        SettlementOutboxResult Enqueue(MatchResultSummary summary);

        /// <summary>当前在盒条数（含装载期恢复的历史条目）。</summary>
        int Count { get; }

        /// <summary>待处理条目（重放/审计面）。</summary>
        IReadOnlyList<MatchResultSummary> ListPending();

        /// <summary>§12 第 4 步"刷新 Outbox 到持久介质"（幂等；WriteThrough 形态下为收口确认）。</summary>
        void Flush();
    }

    /// <summary>
    /// 文件实现的本地持久 Outbox（**宿主层**——文件 IO 在此合法；Runtime/Application 保持零 IO）。
    ///
    /// - **JSONL 日志**：一行一条（DTO 序列化；MatchResultSummary 是只读字段，经内部 DTO 转换）；
    /// - **逐条 write-through**：`FileOptions.WriteThrough` + 每条 Flush——Enqueue 返回时该条已在介质上；
    ///   崩溃窗口最多留下半行，装载期跳过并计数（SettlementReady 是"冻结待提交"事实）；
    /// - **幂等**：matchId 内存集合判重（装载期从日志重建）——重复入盒返回 Duplicate 不重写；
    /// - **有界**：容量上限（配置）；满则 RejectedFull；
    /// - **路径红线**：必须 <c>.journal</c> 后缀（<see cref="RoomServerConfig"/> 装载期强制）——
    ///   RoomServer/Data 的 .json 参与 buildHash 哈希闭包，日志若落在那里会随对局漂移、
    ///   两端握手全拒。缺省路径 Outbox/settlements.journal（.gitignore 已登记）。
    /// </summary>
    public sealed class FileSettlementOutbox : ISettlementOutbox, IDisposable
    {
        private readonly FileStream _stream;
        private readonly HashSet<string> _seen;
        private readonly List<MatchResultSummary> _entries;
        private readonly int _capacity;
        private bool _disposed;

        /// <summary>装载期跳过的坏行数（崩溃半行等；重启不炸——显式计数可观测）。</summary>
        public int SkippedCorruptLines { get; private set; }

        private FileSettlementOutbox(FileStream stream, HashSet<string> seen,
            List<MatchResultSummary> entries, int capacity)
        {
            _stream = stream;
            _seen = seen;
            _entries = entries;
            _capacity = capacity;
        }

        /// <summary>
        /// 打开（或续接）日志：目录不存在则建；既有内容装载入内存（坏行跳过计数），
        /// 新写入**追加**在既有内容之后——进程重启后既有待提交条目不丢（§12 第 4 步的恢复面）。
        /// </summary>
        public static FileSettlementOutbox Open(string path, int capacity)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("日志路径不能为空", nameof(path));
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Outbox 容量必须为正");

            string directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var seen = new HashSet<string>(StringComparer.Ordinal);
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
                    if (seen.Add(entry.MatchId))
                        entries.Add(ToSummary(entry));
                }
            }

            var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read,
                bufferSize: 4096, FileOptions.WriteThrough);
            return new FileSettlementOutbox(stream, seen, entries, capacity) { SkippedCorruptLines = corrupt };
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
            if (_seen.Count >= _capacity) return SettlementOutboxResult.RejectedFull;

            try
            {
                byte[] line = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(ToEntry(summary)));
                _stream.Write(line, 0, line.Length);
                _stream.WriteByte((byte)'\n');
                _stream.Flush();
            }
            catch (Exception ex) when (ex is IOException
                                      || ex is UnauthorizedAccessException
                                      || ex is NotSupportedException
                                      || ex is SecurityException)
            {
                // 介质故障：不抛进权威循环（§6）。内存集合未动——重试同 matchId 仍会尝试追加；
                // 半行残迹由装载期坏行跳过兜住。
                return SettlementOutboxResult.Failed;
            }

            _seen.Add(summary.MatchId);
            _entries.Add(summary);
            return SettlementOutboxResult.Appended;
        }

        public IReadOnlyList<MatchResultSummary> ListPending()
        {
            return _entries.ToArray();
        }

        public void Flush()
        {
            if (_disposed) return;
            _stream.Flush();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _stream.Flush(); } catch { /* 收尾失败不掩盖既有结果 */ }
            _stream.Dispose();
        }

        // ---- 线格式（JSONL；字段名稳定——日志是可回放产物，不做版本协商前不改字段名）----

        private sealed class JournalEntry
        {
            public string MatchId { get; set; }
            public long Seed { get; set; }
            public int FinalFrame { get; set; }
            public int EndReason { get; set; }
            public int[] SeatPlayerIds { get; set; }
        }

        private static JournalEntry ToEntry(MatchResultSummary s) => new JournalEntry
        {
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
