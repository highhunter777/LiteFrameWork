using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RoomServer.Diagnostics
{
    /// <summary>
    /// 记录文件存储：版本化编码 + 同卷原子写（tmp → Move）+ 文件数/总字节保留上限。
    /// 任何 IO 失败都只计数并返回 null——诊断设施不得反噬房间收尾。
    /// </summary>
    public sealed class RoomRunStore
    {
        public const int DefaultMaxFiles = 100;
        public const long DefaultMaxTotalBytes = 512L * 1024 * 1024;
        private const int MaxNamePartChars = 48;

        private readonly string _directory;
        private readonly int _maxFiles;
        private readonly long _maxTotalBytes;

        public long SaveFailures { get; private set; }
        public string LastError { get; private set; }

        public RoomRunStore(string directory, int maxFiles = DefaultMaxFiles,
            long maxTotalBytes = DefaultMaxTotalBytes)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("诊断目录必填", nameof(directory));
            if (maxFiles <= 0) throw new ArgumentOutOfRangeException(nameof(maxFiles));
            if (maxTotalBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxTotalBytes));

            _directory = Path.GetFullPath(directory);
            _maxFiles = maxFiles;
            _maxTotalBytes = maxTotalBytes;
        }

        public string DirectoryPath => _directory;

        public string Save(RoomRunEnvelope envelope)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));
            string tmp = null;
            try
            {
                System.IO.Directory.CreateDirectory(_directory);
                byte[] bytes = RoomRunCodec.Encode(envelope);
                string final = Path.Combine(_directory, BuildFileName(envelope));
                tmp = final + ".tmp";
                File.WriteAllBytes(tmp, bytes);
                File.Move(tmp, final);
                ApplyRetention();
                return final;
            }
            catch (Exception ex)
            {
                SaveFailures++;
                LastError = ex.Message;
                TryDelete(tmp);
                return null;
            }
        }

        public IReadOnlyList<string> List()
        {
            try
            {
                if (!System.IO.Directory.Exists(_directory)) return Array.Empty<string>();
                return System.IO.Directory.GetFiles(_directory, "*.json.gz")
                    .OrderBy(f => f, StringComparer.Ordinal)
                    .ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private string BuildFileName(RoomRunEnvelope envelope)
        {
            RoomRunRecordDto record = envelope.Record;
            string room = Sanitize(record?.RoomId ?? "room");
            string match = Sanitize(record?.MatchId ?? "match");
            DateTime stamp = envelope.EndedAtUtc == default ? DateTime.UtcNow : envelope.EndedAtUtc;
            return $"room-{room}-{match}-{stamp:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.json.gz";
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value)) return "empty";
            char[] invalid = Path.GetInvalidFileNameChars();
            var chars = value.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (Array.IndexOf(invalid, chars[i]) >= 0 || chars[i] == ':') chars[i] = '_';
            string sanitized = new string(chars);
            return sanitized.Length <= MaxNamePartChars ? sanitized : sanitized.Substring(0, MaxNamePartChars);
        }

        private void ApplyRetention()
        {
            var files = System.IO.Directory.GetFiles(_directory, "*.json.gz")
                .Select(f => new FileInfo(f))
                .OrderBy(f => f.LastWriteTimeUtc)
                .ThenBy(f => f.Name, StringComparer.Ordinal)
                .ToList();

            long total = 0;
            for (int i = 0; i < files.Count; i++) total += files[i].Length;

            for (int i = 0; i < files.Count && (files.Count - i > _maxFiles || total > _maxTotalBytes); i++)
            {
                try
                {
                    long length = files[i].Length;
                    files[i].Delete();
                    total -= length;
                }
                catch
                {
                    // 保留失败不阻断：下一次保存再试
                }
            }
        }

        private static void TryDelete(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
