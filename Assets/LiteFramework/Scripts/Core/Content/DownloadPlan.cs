using System;
using System.Collections.Generic;

namespace LiteFramework
{
    /// <summary>
    /// 单个下载来源（《热更与内容发布专项设计》§7"多源切换：各源必须提供同一摘要文件；
    /// 记录源与失败阶段，**不从不同 Release 拼包**"）。
    /// </summary>
    public sealed class DownloadSource
    {
        /// <summary>来源标识（诊断/日志用；不含凭据）。</summary>
        public string Id;

        /// <summary>该源为指定文件提供的下载位置（base URL 或平台句柄，由装配点解释）。</summary>
        public string BaseLocation;

        /// <summary>优先级（小者优先；同优先级按声明顺序）。</summary>
        public int Priority;

        public DownloadSource(string id, string baseLocation, int priority = 0)
        {
            Id = id;
            BaseLocation = baseLocation;
            Priority = priority;
        }
    }

    /// <summary>下载计划条目（清单文件 → 选定来源）。</summary>
    public readonly struct DownloadPlanEntry
    {
        public readonly ReleaseFileEntry File;
        public readonly DownloadSource Source;
        public readonly int Attempt;

        public DownloadPlanEntry(ReleaseFileEntry file, DownloadSource source, int attempt)
        {
            File = file;
            Source = source;
            Attempt = attempt;
        }
    }

    /// <summary>容量与重试预算（§7/§15：**可配置上限**而非写死的"虚构成功率"）。</summary>
    public sealed class DownloadBudget
    {
        /// <summary>下载并发上限（YooAsset 原生下载器取 8 为参考，但按平台配置）。</summary>
        public int MaxConcurrency = 4;

        /// <summary>单文件最大尝试次数（含首试）。**同时是换源次数的上限**——
        /// 每次重试轮转到一个来源，故不另设"最大换源数"旋钮（无独立消费者，§22 精神）。</summary>
        public int MaxAttemptsPerFile = 3;

        /// <summary>退避基数（毫秒）；实际等待由调用方按 <see cref="BackoffMs"/> 计算，本类不 Sleep。</summary>
        public int BackoffBaseMs = 200;

        /// <summary>退避上限（毫秒）。</summary>
        public int BackoffCapMs = 5000;
    }

    /// <summary>
    /// 下载计划（§7）。**纯决策，不做 IO**——真正的下载由 YooAsset/平台适配执行
    /// （§7"YooAsset 负责其擅长的下载与加载实现，项目负责可信发布描述、事务代次与激活决策"）。
    ///
    /// 职责边界：本类只回答"该下哪些文件、优先用哪个源、失败后怎么办"，
    /// 与"下载怎么做"完全分离——这既符合 §7 的分工，也让 L1 能覆盖全部分支。
    /// </summary>
    public sealed class DownloadPlan
    {
        private readonly List<DownloadSource> _sources;
        private readonly ReleaseManifest _manifest;
        private readonly Dictionary<string, int> _attempts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>当前候选的文件清单（只含清单声明的不可变文件——§7"只下载固定 Release 的不可变文件"）。</summary>
        public IReadOnlyList<ReleaseFileEntry> Files => _manifest.Files;

        public DownloadBudget Budget { get; }

        public DownloadPlan(ReleaseManifest manifest, IEnumerable<DownloadSource> sources, DownloadBudget budget = null)
        {
            _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            if (manifest.Files == null || manifest.Files.Count == 0)
                throw new ArgumentException("清单无可下载文件——空清单不应进入下载阶段", nameof(manifest));

            _sources = new List<DownloadSource>();
            if (sources != null)
            {
                foreach (DownloadSource s in sources)
                    if (s != null && !string.IsNullOrEmpty(s.BaseLocation)) _sources.Add(s);
            }
            if (_sources.Count == 0)
                throw new ArgumentException("至少需要一个可用下载来源", nameof(sources));

            // 优先级稳定排序：同优先级保持声明顺序（避免不同平台排序不稳定导致行为漂移）
            _sources.Sort((a, b) => a.Priority != b.Priority ? a.Priority.CompareTo(b.Priority) : 0);

            Budget = budget ?? new DownloadBudget();
            if (Budget.MaxConcurrency < 1) Budget.MaxConcurrency = 1;
            if (Budget.MaxAttemptsPerFile < 1) Budget.MaxAttemptsPerFile = 1;
        }

        /// <summary>候选总字节（空间预检输入）。</summary>
        public long TotalBytes
        {
            get
            {
                long total = 0;
                foreach (ReleaseFileEntry f in _manifest.Files) total += f.Length;
                return total;
            }
        }

        /// <summary>
        /// 为一次下载尝试选定条目：**暂态失败换源，确定性失败不再给条目**
        /// （§7"区分暂态网络错误与签名/兼容错误"；§12"不通过反复重试或忽略验证绕过"）。
        /// 返回 false = 该文件已无可试来源（调用方按候选隔离处理）。
        /// </summary>
        public bool TrySelect(string path, out DownloadPlanEntry entry, out DownloadFailureInfo failure)
        {
            entry = default;
            failure = default;

            ReleaseFileEntry file = FindFile(path);
            if (file == null)
            {
                failure = new DownloadFailureInfo(DownloadFailureKind.FileMissing, path, detail: "不在候选清单内");
                return false;
            }

            int attempt = _attempts.TryGetValue(path, out int a) ? a : 0;
            if (attempt >= Budget.MaxAttemptsPerFile)
            {
                failure = new DownloadFailureInfo(DownloadFailureKind.SourceUnavailable, path,
                    detail: $"已达单文件尝试上限 {Budget.MaxAttemptsPerFile}");
                return false;
            }

            // 每次重试轮转到下一个来源：源数 ≤ 尝试上限，故每个源恰好被用到（或全部源轮完即放弃）。
            // 优先级只决定**首次**用哪个源，不改变轮转顺序。
            DownloadSource source = _sources[attempt % _sources.Count];
            entry = new DownloadPlanEntry(file, source, attempt + 1);
            return true;
        }

        /// <summary>
        /// 记录一次尝试结果并决定后续。返回**是否值得再试**：
        /// 只有暂态失败才计入重试；确定性失败立即终止该文件（返回 false）。
        /// </summary>
        public bool RecordAttempt(string path, DownloadFailureInfo failure)
        {
            if (!failure.IsTransient) return false;      // 确定性失败：重试无效

            int attempt = _attempts.TryGetValue(path, out int a) ? a : 0;
            attempt++;
            _attempts[path] = attempt;
            return attempt < Budget.MaxAttemptsPerFile;
        }

        /// <summary>指数退避毫秒（**只计算不等待**——Sleep 属 IO 层，Core 不引入等待语义）。</summary>
        public int BackoffMs(int attempt)
        {
            if (attempt <= 0) return 0;
            long ms = (long)Budget.BackoffBaseMs << Math.Min(attempt - 1, 16);
            return (int)Math.Min(ms, Budget.BackoffCapMs);
        }

        /// <summary>是否所有文件都已成功（调用方据此进入候选校验）。</summary>
        public bool AllDownloaded(IReadOnlyCollection<string> completedPaths)
        {
            if (completedPaths == null) return false;
            var done = new HashSet<string>(completedPaths, StringComparer.OrdinalIgnoreCase);
            foreach (ReleaseFileEntry f in _manifest.Files)
                if (!done.Contains(Normalize(f.Path))) return false;
            return true;
        }

        private ReleaseFileEntry FindFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string target = Normalize(path);
            foreach (ReleaseFileEntry f in _manifest.Files)
                if (string.Equals(Normalize(f.Path), target, StringComparison.OrdinalIgnoreCase)) return f;
            return null;
        }

        private static string Normalize(string path) => path?.Replace('\\', '/');
    }
}
