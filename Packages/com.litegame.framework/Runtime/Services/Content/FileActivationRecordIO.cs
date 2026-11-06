using LiteFramework;

namespace LiteFramework
{
    /// <summary>
    /// 激活记录的文件持久化（IActivationRecordIO 的 FileSystem 实现——persistentDataPath 下 JSON）。
    /// </summary>
    public sealed class FileActivationRecordIO : IActivationRecordIO
    {
        /// <summary>默认记录路径（FileSystem 相对路径——persistentDataPath 根下）。</summary>
        public const string DefaultRecordPath = "activation.json";

        private const int SupportedSchemaVersion = 1;

        private readonly string _relPath;
        private readonly IFileStore _files;

        public FileActivationRecordIO(string relPath = DefaultRecordPath, IFileStore files = null)
        {
            _relPath = string.IsNullOrEmpty(relPath) ? DefaultRecordPath : relPath;
            _files = files ?? throw new System.ArgumentNullException(nameof(files), "IFileStore 未注入");
        }

        public ActivationRecord TryLoad()
        {
            if (!_files.Exists(_relPath)) return null;
            if (!_files.TryReadJson<ActivationRecord>(_relPath, out var record, out _)) return null;   // 损坏 = 不可信
            if (record == null || record.SchemaVersion != SupportedSchemaVersion) return null;          // 未知结构版本 = 不可信
            if (!ActivationRecordIntegrity.Verify(record)) return null;                                // 完整性不符 = 不可信
            return record;
        }

        public void Save(ActivationRecord record)
        {
            _files.WriteJson(_relPath, record);   // 临时文件 + Replace（原子切换）
        }
    }
}
