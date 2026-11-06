using System;

namespace LiteFramework
{
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
}
