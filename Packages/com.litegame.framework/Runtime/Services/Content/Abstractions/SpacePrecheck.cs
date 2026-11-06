using System;

namespace LiteFramework
{
    /// <summary>
    /// 空间预检。**溢出安全**：各分项均为 long，累加用 checked 语义的显式判定，
    /// 溢出即判"不足"而不是回绕成小数——回绕会让"空间不够"变成"空间充足"，是安全缺陷。
    /// </summary>
    public static class SpacePrecheck
    {
        /// <summary>按请求计算所需空间并判定。</summary>
        public static SpaceCheckResult Evaluate(SpaceCheckRequest request, long availableBytes)
        {
            if (request == null) return SpaceCheckResult.Fail(0, availableBytes, "请求为空");

            long parts;
            try
            {
                parts = checked(request.CandidateBytes + request.DecompressPeakBytes
                                + request.RetainedVersionBytes + request.SafetyMarginBytes);
            }
            catch (OverflowException)
            {
                return SpaceCheckResult.Fail(long.MaxValue, availableBytes, "分项累加溢出——按不足处理");
            }

            if (parts < 0)
                return SpaceCheckResult.Fail(parts, availableBytes, "分项含负值——按不足处理");

            if (availableBytes < 0)
                return SpaceCheckResult.Fail(parts, availableBytes, "可用空间不可知（需平台提供预检）");

            if (availableBytes < parts)
                return SpaceCheckResult.Fail(parts, availableBytes,
                    $"缺口 {parts - availableBytes} 字节（候选 {request.CandidateBytes} + 解压峰值 {request.DecompressPeakBytes}" +
                    $" + 保留版本 {request.RetainedVersionBytes} + 余量 {request.SafetyMarginBytes}）");

            return SpaceCheckResult.Pass(parts, availableBytes);
        }
    }
}
