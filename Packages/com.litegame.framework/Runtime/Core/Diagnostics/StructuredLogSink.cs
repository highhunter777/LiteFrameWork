using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace LiteFramework.Diagnostics
{
    /// <summary>
    /// 结构化日志出口：把 <see cref="LogEntry"/> 写成 **JSON Lines**（每行一个对象，UTF-8 无 BOM），
    /// 供 `scripts/logs/query.py` 按对局/席位/帧/标签/级别检索。
    ///
    /// **调用线程只入队**（有界环形；满则丢最旧并计数），格式化/落盘在后台线程；按大小轮转、
    /// 按文件数清理。格式化复用 StringBuilder + char 缓冲（不逐行 ToString）；Sink 自身故障只记
    /// <see cref="LastError"/>，不向业务抛异常。线程随 <see cref="Dispose"/> 收口。
    /// </summary>
    public sealed class StructuredLogSink : ILogSink, IDisposable
    {
        private readonly string _directory;
        private readonly long _maxBytesPerFile;
        private readonly int _maxFiles;
        private readonly object _gate = new object();
        private readonly LogEntry[] _queue;
        private readonly LogEntry[] _batch;
        private char[] _chars = new char[4096];
        private readonly StringBuilder _line = new StringBuilder(512);
        private readonly string _sessionId = Log.CurrentSessionId;

        private int _head;
        private int _count;
        private int _part;
        private long _fileBytes;
        private long _written;
        private long _dropped;
        private long _lastFlushTick;
        private bool _flushRequested;
        private bool _disposed;
        private volatile bool _stop;
        private volatile string _lastError = string.Empty;
        private volatile string _currentPath = string.Empty;

        private readonly AutoResetEvent _signal = new AutoResetEvent(false);
        private readonly ManualResetEventSlim _flushed = new ManualResetEventSlim(false);

        private Thread _thread;
        private StreamWriter _writer;

        /// <param name="directory">日志目录（客户端 = `persistentDataPath/logs`；测试用临时目录）。</param>
        /// <param name="capacity">入队环容量（满则丢最旧并计数）。</param>
        /// <param name="maxBytesPerFile">单文件字节上限（近似按行字符数计）；达到即轮转。</param>
        /// <param name="maxFiles">目录内保留的日志文件数（超出按修改时间删最旧）。</param>
        public StructuredLogSink(string directory, int capacity = 1024,
            long maxBytesPerFile = 8L * 1024 * 1024, int maxFiles = 8)
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("日志目录不能为空", nameof(directory));
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (maxBytesPerFile < 4096) throw new ArgumentOutOfRangeException(nameof(maxBytesPerFile));
            if (maxFiles < 1) throw new ArgumentOutOfRangeException(nameof(maxFiles));

            _directory = directory;
            _maxBytesPerFile = maxBytesPerFile;
            _maxFiles = maxFiles;
            _queue = new LogEntry[capacity];
            _batch = new LogEntry[Math.Min(capacity, 64)];
        }

        /// <summary>已写出的条目数（后台线程推进）。</summary>
        public long Written => Interlocked.Read(ref _written);
        /// <summary>因队列满被丢弃的条目数（诊断面：丢日志要可见）。</summary>
        public long Dropped => Interlocked.Read(ref _dropped);
        /// <summary>当前日志文件（未开始/未写入时为空）。</summary>
        public string CurrentPath => _currentPath;
        /// <summary>最近一次写盘/IO 错误（空 = 无）；只记录，不抛。</summary>
        public string LastError => _lastError;

        /// <summary>启动后台写线程（幂等）。目录创建/旧文件清理在这里做。</summary>
        public void Start()
        {
            lock (_gate)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(StructuredLogSink));
                if (_thread != null) return;
                try { Directory.CreateDirectory(_directory); }
                catch (Exception ex) { _lastError = ex.Message; return; }
                PruneOldFiles();
                _thread = new Thread(Loop) { IsBackground = true, Name = "StructuredLogSink" };
                _thread.Start();
            }
        }

        /// <inheritdoc />
        public void Write(in LogEntry entry)
        {
            lock (_gate)
            {
                if (_disposed) return;
                if (_count == _queue.Length)
                {
                    _head = (_head + 1) % _queue.Length;   // 丢最旧：诊断日志优先保最新现场
                    _count--;
                    _dropped++;
                }
                _queue[(_head + _count) % _queue.Length] = entry;
                _count++;
            }
            _signal.Set();
        }

        /// <summary>等待队列排空并刷盘（测试/故障现场用；超时返回 false）。</summary>
        public bool Flush(TimeSpan timeout)
        {
            if (_thread == null) return false;
            _flushed.Reset();
            lock (_gate) { _flushRequested = true; }
            _signal.Set();
            return _flushed.Wait(timeout);
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                _stop = true;
            }
            _signal.Set();
            Thread thread = _thread;
            _thread = null;
            if (thread != null) thread.Join(2000);

            Drain();
            FlushWriter();
            if (_writer != null)
            {
                try { _writer.Dispose(); } catch { /* 关闭期 IO 失败不反噬 */ }
                _writer = null;
            }
            _signal.Dispose();
            _flushed.Dispose();
        }

        private void Loop()
        {
            while (true)
            {
                _signal.WaitOne(250);
                Drain();
                if (_stop) break;

                bool requested;
                lock (_gate)
                {
                    requested = _flushRequested;
                    _flushRequested = false;
                }
                if (requested)
                {
                    FlushWriter();
                    _flushed.Set();
                }
                else if (Environment.TickCount - _lastFlushTick > 1000)
                {
                    FlushWriter();
                }
            }
            Drain();
            FlushWriter();
        }

        private void Drain()
        {
            while (true)
            {
                int take = 0;
                lock (_gate)
                {
                    while (take < _batch.Length && _count > 0)
                    {
                        _batch[take++] = _queue[_head];
                        _head = (_head + 1) % _queue.Length;
                        _count--;
                    }
                }
                if (take == 0) return;
                for (int i = 0; i < take; i++) WriteEntry(in _batch[i]);
                Array.Clear(_batch, 0, take);   // 放开字符串引用
            }
        }

        private void WriteEntry(in LogEntry entry)
        {
            try
            {
                EnsureWriter();
                _line.Clear();
                _line.Append("{\"ts\":\"").Append(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture))
                     .Append("\",\"lvl\":\"").Append(LevelName(entry.Level))
                     .Append("\",\"tag\":\"").Append(Log.TagName(entry.Tag))
                     .Append("\",\"sid\":\"");
                JsonText.AppendEscaped(_line, entry.SessionId);
                _line.Append("\",\"mid\":\"");
                JsonText.AppendEscaped(_line, entry.MatchId);
                _line.Append("\",\"f\":").Append(entry.Frame)
                     .Append(",\"p\":").Append(entry.PlayerId)
                     .Append(",\"msg\":\"");
                JsonText.AppendEscaped(_line, entry.Message);
                _line.Append("\"}\n");

                int length = _line.Length;
                if (length > _chars.Length) _chars = new char[length * 2];
                _line.CopyTo(0, _chars, 0, length);
                _writer.Write(_chars, 0, length);
                _fileBytes += length;
                Interlocked.Increment(ref _written);

                if (_fileBytes >= _maxBytesPerFile)
                {
                    _writer.Dispose();
                    _writer = null;
                    PruneOldFiles();
                }
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
            }
        }

        private void EnsureWriter()
        {
            if (_writer != null) return;
            Directory.CreateDirectory(_directory);
            _part++;
            string name = string.Format(CultureInfo.InvariantCulture, "log-{0}-{1:yyyyMMdd-HHmmss}-p{2}.jsonl",
                _sessionId, DateTime.UtcNow, _part);
            _currentPath = Path.Combine(_directory, name);
            var stream = new FileStream(_currentPath, FileMode.Create, FileAccess.Write, FileShare.Read, 64 * 1024);
            _writer = new StreamWriter(stream, new UTF8Encoding(false));
            _fileBytes = 0;
        }

        private void FlushWriter()
        {
            if (_writer == null) return;
            try { _writer.Flush(); }
            catch (Exception ex) { _lastError = ex.Message; }
            _lastFlushTick = Environment.TickCount;
        }

        private void PruneOldFiles()
        {
            try
            {
                var directory = new DirectoryInfo(_directory);
                if (!directory.Exists) return;
                FileInfo[] files = directory.GetFiles("log-*.jsonl");
                if (files.Length <= _maxFiles) return;
                Array.Sort(files, (a, b) => a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc));
                int remove = files.Length - _maxFiles;
                for (int i = 0; i < remove; i++)
                {
                    try { files[i].Delete(); } catch { /* 被占用/权限：跳过 */ }
                }
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
            }
        }

        private static string LevelName(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Info: return "Info";
                case LogLevel.Warning: return "Warning";
                case LogLevel.Error: return "Error";
                case LogLevel.Fatal: return "Fatal";
                default: return "Unknown";
            }
        }

    }
}
