namespace LiteGame.Editor.UITools.Page
{
    /// <summary>写表结果：**备份先于写**，失败/超时也回传 <see cref="BackupPath"/>（登记核验回滚用）。</summary>
    public sealed class UIPageTableWriteResult
    {
        public bool Ok;
        public string BackupPath;
        public string Error;

        public static UIPageTableWriteResult Success(string backupPath)
            => new UIPageTableWriteResult { Ok = true, BackupPath = backupPath };

        public static UIPageTableWriteResult Failure(string error, string backupPath = null)
            => new UIPageTableWriteResult { Ok = false, Error = error, BackupPath = backupPath };
    }
}
