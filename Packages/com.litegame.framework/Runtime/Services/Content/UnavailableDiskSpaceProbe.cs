namespace LiteFramework
{
    /// <summary>
    /// 候选磁盘余量探测（§7"空间预检"）。
    /// </summary>
    public sealed class UnavailableDiskSpaceProbe : IDiskSpaceProbe
    {
        public long GetAvailableBytes() => -1L;
    }
}
