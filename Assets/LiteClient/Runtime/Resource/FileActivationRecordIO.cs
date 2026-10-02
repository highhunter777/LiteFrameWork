using LiteFramework;

namespace LiteClient
{
    /// <summary>
    /// 激活记录的文件持久化（IActivationRecordIO 的 FileSys 实现——persistentDataPath 下 JSON）。
    ///
    /// 契约落点（《热更与内容发布专项设计》§8）：
    /// - **不可信即弃**：缺文件 / 解析失败 / **结构版本不认识** / **完整性校验不符** → 返回 null
    ///   （调用方按全新安装回 builtin 起点，fail-safe——记录损坏不能阻止以内置内容启动）。
    /// - **原子提交**：<see cref="FileSys.WriteJson"/> 走的是
    ///   `临时文件 + File.Replace/Move`（见 <c>FileSys.CommitAtomic</c>），**不是**裸覆盖写——
    ///   "写一半"被挡在目标文件之外。
    /// - **完整性保护**：即便有 Replace，仍无法排除文件系统还原、外部工具改写、旧备份回填；
    ///   且被截断但仍合法的 JSON 会被当作有效记录。故提交前经 <see cref="ActivationRecordIntegrity.Stamp"/>
    ///   盖章，读入时校验，不符即按不可信处理（§8"状态记录…带完整性保护"）。
    /// - **未覆盖**：`File.Replace` 的 fsync/掉电语义与杀进程中断矩阵需真机验证。
    /// </summary>
    public sealed class FileActivationRecordIO : IActivationRecordIO
    {
        /// <summary>默认记录路径（FileSys 相对路径——persistentDataPath 根下）。</summary>
        public const string DefaultRecordPath = "activation.json";

        private const int SupportedSchemaVersion = 1;

        private readonly string _relPath;

        public FileActivationRecordIO(string relPath = DefaultRecordPath)
        {
            _relPath = string.IsNullOrEmpty(relPath) ? DefaultRecordPath : relPath;
        }

        public ActivationRecord TryLoad()
        {
            if (!FileSys.Exists(_relPath)) return null;
            if (!FileSys.TryReadJson<ActivationRecord>(_relPath, out var record, out _)) return null;   // 损坏 = 不可信
            if (record == null || record.SchemaVersion != SupportedSchemaVersion) return null;          // 未知结构版本 = 不可信
            if (!ActivationRecordIntegrity.Verify(record)) return null;                                // 完整性不符 = 不可信
            return record;
        }

        public void Save(ActivationRecord record)
        {
            FileSys.WriteJson(_relPath, record);   // 临时文件 + Replace（原子切换）
        }
    }
}
