using System;
using LiteFramework;

namespace LiteClient
{
    /// <summary>
    /// 磁盘余量探测（**桌面平台的诚实实现**；移动端回退不可知）。
    ///
    /// 用 <see cref="FileSys.GetAvailableBytes"/> 查询 <c>persistentDataPath</c> 所在卷的可用空间：
    /// - **桌面（Editor / Standalone）** → 真实可用字节；
    /// - **移动端 / 不支持时** → -1 = 不可知（<see cref="SpacePrecheck"/> 按不足处理，fail-closed）。
    ///
    /// **只读、不探测**：不创建/删除任何文件，纯粹委托门面。
    /// </summary>
    public sealed class DriveInfoSpaceProbe : IDiskSpaceProbe
    {
        private readonly string _relDir;

        /// <param name="relDir">目标目录（<b>FileSys 相对路径</b>，如 <c>.</c> 表示 RootPath 所在卷）。</param>
        public DriveInfoSpaceProbe(string relDir)
        {
            if (string.IsNullOrEmpty(relDir)) throw new ArgumentNullException(nameof(relDir));
            _relDir = relDir;
        }

        public long GetAvailableBytes()
        {
            long available = FileSys.GetAvailableBytes(_relDir);
            if (available < 0)
            {
                // 静默 -1 会让"桌面拿不到余量"成为不可见事故（SpacePrecheck 按不足拒绝所有候选，
                // 热更链在 Player 上整体不可用）——留一次可诊断痕迹（每次预检至多一条，非热路径）。
                Log.Warning($"磁盘余量不可知（path='{_relDir}'）——空间预检将按不足处理", "Content");
            }
            return available;
        }
    }

    /// <summary>
    /// 写探针（**通用实现**；<see cref="DriveInfoSpaceProbe"/> 探不到时的回退）。
    ///
    /// 策略：先试桌面精确路径（廉价）；不可用时回退 <see cref="FileSys.WriteProbe"/>——
    /// 向目标目录逐级试探写入固定块直到失败，得出"至少能写多少"。写探针拿到的是**下界**，
    /// 故报告值保守（宁可少报也不多报：多报会让预检放行、更新写到一半失败）。
    ///
    /// **代价与边界（如实标注）**：
    /// - 写探针会**实际占用并删除**临时文件；在低存储设备上可能触发系统清理——故只在
    ///   精确探测不可用时走这条路，且探针上限远低于真实盘容量；
    /// - 它测量的是"**当前能写多少**"，不是"总剩余空间"——有配额/写保护的目录会被如实判小；
    /// - **不是精确计量**：报告值只够做"够/不够"的判定，不能用于展示给用户的容量数字。
    ///
    /// **IO 纪律**：写路径经 <see cref="FileSys"/>（<see cref="FileSys.WriteProbe"/>），
    /// 本类不直接触碰 <see cref="System.IO"/>——IO 唯一入口是门面。
    /// </summary>
    public sealed class WriteProbeDiskSpaceProbe : IDiskSpaceProbe
    {
        private readonly string _relDir;
        private readonly long _probeCapBytes;
        private readonly DriveInfoSpaceProbe _desktop;

        /// <param name="relDir">目标目录（FileSys 相对路径）。</param>
        /// <param name="probeCapBytes">写探针上限（默认 512MB；避免在低存储设备上制造压力）。</param>
        public WriteProbeDiskSpaceProbe(string relDir, long probeCapBytes = 512L * 1024 * 1024)
        {
            if (string.IsNullOrEmpty(relDir)) throw new ArgumentNullException(nameof(relDir));
            _relDir = relDir;
            _probeCapBytes = probeCapBytes < 1024 * 1024 ? 1024 * 1024 : probeCapBytes;
            _desktop = new DriveInfoSpaceProbe(relDir);
        }

        public long GetAvailableBytes()
        {
            // ① 精确路径（桌面）
            long desktop = _desktop.GetAvailableBytes();
            if (desktop >= 0) return desktop;

            // ② 通用写探针（下界）
            return FileSys.WriteProbe(_relDir, capBytes: _probeCapBytes);
        }
    }

    /// <summary>
    /// 候选磁盘余量探测（§7"空间预检"）。
    ///
    /// **诚实实现**：返回 **-1 = 不可知**——
    /// 而 <see cref="SpacePrecheck"/> 对"不可知"按**不足**处理（不会让更新在写入中途失败）。
    ///
    /// 这意味着**当前形态下空间预检会拒绝所有候选**：这是刻意的 fail-closed，
    /// 而非缺陷。需要探测时改用 <see cref="DriveInfoSpaceProbe"/>（桌面精确）或
    /// <see cref="WriteProbeDiskSpaceProbe"/>（以写入压力换通用性）。
    /// </summary>
    public sealed class UnavailableDiskSpaceProbe : IDiskSpaceProbe
    {
        public long GetAvailableBytes() => -1L;
    }
}
