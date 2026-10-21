using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace LiteFramework.Diagnostics
{
    /// <summary>客户端现场包请求（纯数据；调用方负责从 Unity 侧取日志/路径）。</summary>
    public sealed class DiagnosticsBundleRequest
    {
        public string Reason { get; set; }
        public string MatchId { get; set; }
        public string RoomId { get; set; }
        public int Frame { get; set; }
        public long LocalEntityId { get; set; }
        public int ReconcileCount { get; set; }
        public string BuildHash { get; set; }
        public uint ConfigDigest { get; set; }
        public string Platform { get; set; }
        public string AppVersion { get; set; }
        public IReadOnlyList<string> RecentLogs { get; set; }
        public string PlayerLogTail { get; set; }
        public string ExtraJson { get; set; }
    }

    /// <summary>
    /// 客户端现场包写入器（零引擎依赖，L1 可测）：目录 + manifest + 最近日志 + Player.log 尾。
    /// 同对局限频/限量；任何 IO 失败返回 null，不反噬对局。
    /// </summary>
    public sealed class DiagnosticsBundleWriter
    {
        private const int SchemaVersion = 1;
        private const int MinFileBudgetBytes = 256;

        private readonly string _rootDirectory;
        private readonly int _maxBundlesPerMatch;
        private readonly TimeSpan _minInterval;
        private readonly long _maxBytes;
        private readonly Dictionary<string, MatchBucket> _buckets = new Dictionary<string, MatchBucket>(StringComparer.Ordinal);

        private sealed class MatchBucket
        {
            public int Count;
            public DateTime LastUtc;
        }

        public DiagnosticsBundleWriter(string rootDirectory, int maxBundlesPerMatch = 3,
            TimeSpan? minInterval = null, long maxBytes = 2L * 1024 * 1024)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
                throw new ArgumentException("现场包根目录必填", nameof(rootDirectory));
            if (maxBundlesPerMatch <= 0) throw new ArgumentOutOfRangeException(nameof(maxBundlesPerMatch));
            if (maxBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));

            _rootDirectory = Path.GetFullPath(rootDirectory);
            _maxBundlesPerMatch = maxBundlesPerMatch;
            _minInterval = minInterval ?? TimeSpan.FromSeconds(30);
            _maxBytes = maxBytes;
        }

        /// <summary>限频/限量检查（不消费配额；供调用方在组装高成本请求前提前返回）。</summary>
        public bool CanAccept(string matchId)
        {
            string key = matchId ?? string.Empty;
            if (!_buckets.TryGetValue(key, out MatchBucket bucket)) return true;
            if (bucket.Count >= _maxBundlesPerMatch) return false;
            if (bucket.Count > 0 && _minInterval > TimeSpan.Zero && DateTime.UtcNow - bucket.LastUtc < _minInterval)
                return false;
            return true;
        }

        public string Write(DiagnosticsBundleRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            string matchKey = request.MatchId ?? string.Empty;
            DateTime now = DateTime.UtcNow;

            if (!TryAcquire(matchKey, now)) return null;

            string dir = null;
            try
            {
                Directory.CreateDirectory(_rootDirectory);
                dir = CreateBundleDirectory(request, now);
                Directory.CreateDirectory(dir);

                int fileBudget = Math.Max(MinFileBudgetBytes, (int)Math.Min(int.MaxValue, _maxBytes / 4));
                string recent = JoinLogs(request.RecentLogs, fileBudget);
                string playerTail = TruncateTail(request.PlayerLogTail, fileBudget);

                File.WriteAllText(Path.Combine(dir, "log-recent.txt"), recent);
                File.WriteAllText(Path.Combine(dir, "player-log-tail.txt"), playerTail);
                File.WriteAllText(Path.Combine(dir, "mismatch.json"), BuildMismatchJson(request, now));
                // 附加上下文（如托管异常文案+堆栈尾）：只在调用方给了才落件——文件清单同步
                if (!string.IsNullOrEmpty(request.ExtraJson))
                    File.WriteAllText(Path.Combine(dir, "extra.txt"), request.ExtraJson);
                File.WriteAllText(Path.Combine(dir, "manifest.json"), BuildManifestJson(request, now));
                return dir;
            }
            catch
            {
                if (dir != null)
                {
                    try { Directory.Delete(dir, true); } catch { }
                }
                return null;
            }
        }

        private bool TryAcquire(string matchKey, DateTime now)
        {
            if (!_buckets.TryGetValue(matchKey, out MatchBucket bucket))
            {
                bucket = new MatchBucket();
                _buckets[matchKey] = bucket;
            }
            if (bucket.Count >= _maxBundlesPerMatch) return false;
            if (bucket.Count > 0 && _minInterval > TimeSpan.Zero && now - bucket.LastUtc < _minInterval)
                return false;

            bucket.Count++;
            bucket.LastUtc = now;
            return true;
        }

        private string CreateBundleDirectory(DiagnosticsBundleRequest request, DateTime now)
        {
            string reason = Sanitize(string.IsNullOrEmpty(request.Reason) ? "diagnostics" : request.Reason, 32);
            string name = string.Format(CultureInfo.InvariantCulture, "diag-{0:yyyyMMdd-HHmmssfff}-{1}-{2}",
                now, reason, Guid.NewGuid().ToString("N").Substring(0, 8));
            return Path.Combine(_rootDirectory, name);
        }

        private static string JoinLogs(IReadOnlyList<string> logs, int budget)
        {
            if (logs == null || logs.Count == 0) return string.Empty;
            var sb = new StringBuilder(Math.Min(budget, 4096));
            for (int i = 0; i < logs.Count; i++)
            {
                if (i > 0) sb.Append('\n');
                sb.Append(logs[i] ?? string.Empty);
            }
            return TruncateTail(sb.ToString(), budget);
        }

        private static string TruncateTail(string text, int budget)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Length <= budget ? text : text.Substring(text.Length - budget);
        }

        private string BuildManifestJson(DiagnosticsBundleRequest request, DateTime now)
        {
            var sb = new StringBuilder(512);
            sb.Append('{');
            AppendNumber(sb, "schemaVersion", SchemaVersion, first: true);
            AppendString(sb, "capturedAtUtc", now.ToString("O", CultureInfo.InvariantCulture));
            AppendString(sb, "reason", request.Reason);
            AppendString(sb, "matchId", request.MatchId);
            AppendString(sb, "roomId", request.RoomId);
            AppendNumber(sb, "frame", request.Frame);
            AppendNumber(sb, "localEntityId", request.LocalEntityId);
            AppendNumber(sb, "reconcileCount", request.ReconcileCount);
            AppendString(sb, "buildHash", request.BuildHash);
            AppendNumber(sb, "configDigest", request.ConfigDigest);
            AppendString(sb, "platform", request.Platform);
            AppendString(sb, "appVersion", request.AppVersion);
            AppendString(sb, "files", string.IsNullOrEmpty(request.ExtraJson)
                ? "manifest.json,log-recent.txt,player-log-tail.txt,mismatch.json"
                : "manifest.json,log-recent.txt,player-log-tail.txt,mismatch.json,extra.txt");
            sb.Append('}');
            return sb.ToString();
        }

        private static string BuildMismatchJson(DiagnosticsBundleRequest request, DateTime now)
        {
            var sb = new StringBuilder(256);
            sb.Append('{');
            AppendNumber(sb, "frame", request.Frame, first: true);
            AppendString(sb, "reason", request.Reason);
            AppendString(sb, "matchId", request.MatchId);
            AppendString(sb, "roomId", request.RoomId);
            AppendNumber(sb, "localEntityId", request.LocalEntityId);
            AppendNumber(sb, "reconcileCount", request.ReconcileCount);
            AppendString(sb, "capturedAtUtc", now.ToString("O", CultureInfo.InvariantCulture));
            sb.Append('}');
            return sb.ToString();
        }

        private static void AppendNumber(StringBuilder sb, string name, long value, bool first = false)
        {
            if (!first) sb.Append(',');
            AppendEscaped(sb, name);
            sb.Append(':').Append(value.ToString(CultureInfo.InvariantCulture));
        }

        private static void AppendString(StringBuilder sb, string name, string value)
        {
            sb.Append(',');
            AppendEscaped(sb, name);
            sb.Append(':');
            AppendEscaped(sb, value ?? string.Empty);
        }

        private static void AppendEscaped(StringBuilder sb, string value)
        {
            sb.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        private static string Sanitize(string value, int maxChars)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            var chars = value.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (Array.IndexOf(invalid, chars[i]) >= 0 || chars[i] == ':') chars[i] = '_';
            string s = new string(chars);
            return s.Length <= maxChars ? s : s.Substring(0, maxChars);
        }
    }
}
