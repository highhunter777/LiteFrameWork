using System;

namespace LiteFramework
{
    /// <summary>
    /// 磁盘余量探测（**桌面平台的诚实实现**；移动端回退不可知）。
    /// </summary>
    public sealed class DriveInfoSpaceProbe : IDiskSpaceProbe
    {
        private readonly string _relDir;
        private readonly IFileStore _files;

        /// <param name="relDir">目标目录（<b>IFileStore 相对路径</b>，如 <c>.</c> 表示 RootPath 所在卷）。</param>
        public DriveInfoSpaceProbe(string relDir, IFileStore files = null)
        {
            if (string.IsNullOrEmpty(relDir)) throw new ArgumentNullException(nameof(relDir));
            _relDir = relDir;
            _files = files ?? throw new System.ArgumentNullException(nameof(files), "IFileStore 未注入");
        }

        public long GetAvailableBytes()
        {
            long available = _files.GetAvailableBytes(_relDir);
            if (available < 0)
            {
                // 静默 -1 会让"桌面拿不到余量"成为不可见事故（SpacePrecheck 按不足拒绝所有候选，
                // 热更链在 Player 上整体不可用）——留一次可诊断痕迹（每次预检至多一条，非热路径）。
                Log.Warning($"磁盘余量不可知（path='{_relDir}'）——空间预检将按不足处理", LogTag.Content);
            }
            return available;
        }
    }
}
