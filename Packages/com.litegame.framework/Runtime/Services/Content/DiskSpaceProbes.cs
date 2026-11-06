using System;
using LiteFramework;

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

    /// <summary>
    /// 写探针（**通用实现**；<see cref="DriveInfoSpaceProbe"/> 探不到时的回退）。
    /// </summary>
    public sealed class WriteProbeDiskSpaceProbe : IDiskSpaceProbe
    {
        private readonly string _relDir;
        private readonly long _probeCapBytes;
        private readonly DriveInfoSpaceProbe _desktop;
        private readonly IFileStore _files;

        /// <param name="relDir">目标目录（IFileStore 相对路径）。</param>
        /// <param name="probeCapBytes">写探针上限（默认 512MB；避免在低存储设备上制造压力）。</param>
        public WriteProbeDiskSpaceProbe(string relDir, long probeCapBytes = 512L * 1024 * 1024,
            IFileStore files = null)
        {
            if (string.IsNullOrEmpty(relDir)) throw new ArgumentNullException(nameof(relDir));
            _relDir = relDir;
            _probeCapBytes = probeCapBytes < 1024 * 1024 ? 1024 * 1024 : probeCapBytes;
            _files = files ?? throw new System.ArgumentNullException(nameof(files), "IFileStore 未注入");
            _desktop = new DriveInfoSpaceProbe(relDir, _files);
        }

        public long GetAvailableBytes()
        {
            // ① 精确路径（桌面）。
            long desktop = _desktop.GetAvailableBytes();
            if (desktop >= 0) return desktop;

            // ② 通用写探针（下界）。
            return _files.WriteProbe(_relDir, capBytes: _probeCapBytes);
        }
    }

    /// <summary>
    /// 候选磁盘余量探测（§7"空间预检"）。
    /// </summary>
    public sealed class UnavailableDiskSpaceProbe : IDiskSpaceProbe
    {
        public long GetAvailableBytes() => -1L;
    }
}
