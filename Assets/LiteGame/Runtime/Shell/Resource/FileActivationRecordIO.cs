using LiteFramework;

namespace LiteGame
{
    /// <summary>
    /// 激活记录的文件持久化（C1-⑩：IActivationRecordIO 的 FileSys 实现——persistentDataPath 下 JSON）。
    ///
    /// 契约落点（《热更与内容发布专项设计》§8）：
    /// - **不可信即弃**：缺文件/解析失败/**结构版本不认识** → 返回 null（调用方按全新安装回 builtin 起点，
    ///   fail-safe——记录损坏不能阻止以内置内容启动）。
    /// - **最小写实现**：FileSys.WriteJson 单文件全量覆盖。**非原子**——写盘中断窗口的最坏后果 =
    ///   记录损坏 → 下次 TryLoad 返回 null → 回 builtin（安全但有损回退）。临时文件 + File.Replace
    ///   的原子切换与中断测试矩阵（杀进程/写一半/重命名失败）随热更批补强——本批不把覆盖写称为原子事务。
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
            return record;
        }

        public void Save(ActivationRecord record)
        {
            FileSys.WriteJson(_relPath, record);
        }
    }
}
