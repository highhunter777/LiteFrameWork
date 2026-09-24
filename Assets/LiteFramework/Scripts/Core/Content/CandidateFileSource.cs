using System;
using System.Collections.Generic;

namespace LiteFramework
{
    /// <summary>
    /// 候选文件读句柄（《热更与内容发布专项设计》§7"下载、候选校验与容量"）。
    ///
    /// 只暴露校验需要的两件事：**长度**与**完整字节**。为什么不做流式：
    /// 候选摘要按 §5 对**原始字节**计算完整 SHA-256，<see cref="ContentHash.Hasher"/> 需整块输入；
    /// 且 <see cref="ReleaseBudget.MaxFileBytes"/> 已对单文件设上限（默认 512MB），
    /// 逐文件整体读入有界。流式分块属于大包优化，在有真实包规模数据前不预先引入
    /// （§15"没有目标包规模与设备数据时不虚构"）。
    /// </summary>
    public interface ICandidateFile
    {
        /// <summary>相对候选根的正斜杠路径（与 <see cref="ReleaseFileEntry.Path"/> 同形）。</summary>
        string Path { get; }

        /// <summary>实际字节长度。</summary>
        long Length { get; }

        /// <summary>读取完整字节；失败返回 null（视为缺失/不可读——调用方按校验失败处理，不抛）。</summary>
        byte[] ReadAll();
    }

    /// <summary>
    /// 候选文件访问端口（IO 由装配点提供：Player 用文件系统，测试用内存假件）。
    /// **Core 不依赖 System.IO 具体实现**——与 <see cref="IActivationRecordIO"/> 同一纪律。
    /// </summary>
    public interface ICandidateFileSource
    {
        /// <summary>打开一个候选文件；不存在/不可读返回 null（调用方按缺失处理）。</summary>
        ICandidateFile Open(string path);
    }

    /// <summary>
    /// 磁盘余量端口（§7"空间预检：计入候选、临时/解压峰值、保留版本及余量"）。
    /// 单独成端口而非并入 <see cref="ICandidateFileSource"/>：余量查询与文件读取的可用性不同
    /// （平台可能能读文件却拿不到配额），且预检失败必须有明确结果而不是静默跳过。
    /// </summary>
    public interface IDiskSpaceProbe
    {
        /// <summary>候选落盘位置的可用字节数；不可知返回 -1
        /// （<see cref="SpacePrecheck.Evaluate"/> 据此按"不足"拒绝——不可预检不能被当作"空间充足"）。</summary>
        long GetAvailableBytes();
    }

    /// <summary>
    /// 空间预检请求（§7）。调用方给出各分项，本类型只做**可测的算术与判定**。
    /// </summary>
    public sealed class SpaceCheckRequest
    {
        /// <summary>候选文件总字节（= 清单 <see cref="ReleaseFileEntry.Length"/> 之和）。</summary>
        public long CandidateBytes;

        /// <summary>解压峰值（未压缩包为 0）。</summary>
        public long DecompressPeakBytes;

        /// <summary>保留版本的既有占用（不得为其腾挪而破坏可用/恢复版本——§12）。</summary>
        public long RetainedVersionBytes;

        /// <summary>安全余量（平台策略给值；§15 不虚构固定值）。</summary>
        public long SafetyMarginBytes;
    }

    /// <summary>空间预检结果（稳定结果码 + 占比明细，便于定位"是谁撑爆了"。）</summary>
    public readonly struct SpaceCheckResult
    {
        public readonly bool Passed;
        public readonly long RequiredBytes;
        public readonly long AvailableBytes;
        public readonly string Detail;

        private SpaceCheckResult(bool passed, long required, long available, string detail)
        {
            Passed = passed;
            RequiredBytes = required;
            AvailableBytes = available;
            Detail = detail;
        }

        internal static SpaceCheckResult Pass(long required, long available)
            => new SpaceCheckResult(true, required, available, null);

        internal static SpaceCheckResult Fail(long required, long available, string detail)
            => new SpaceCheckResult(false, required, available, detail);

        public override string ToString()
            => Passed ? $"预检通过（需 {RequiredBytes} / 可用 {AvailableBytes}）"
                      : $"预检不足（需 {RequiredBytes} / 可用 {AvailableBytes}）: {Detail}";
    }

    /// <summary>
    /// 空间预检（§7）。**溢出安全**：各分项均为 long，累加用 checked 语义的显式判定，
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
