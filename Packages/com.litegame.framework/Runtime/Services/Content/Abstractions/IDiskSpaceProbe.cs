namespace LiteFramework
{
    /// <summary>
    /// 磁盘余量端口（空间预检：计入候选、临时/解压峰值、保留版本及余量）。
    /// </summary>
    public interface IDiskSpaceProbe
    {
        /// <summary>候选落盘位置的可用字节数；不可知返回 -1
        /// （<see cref="SpacePrecheck.Evaluate"/> 据此按"不足"拒绝——不可预检不能被当作"空间充足"）。</summary>
        long GetAvailableBytes();
    }
}
