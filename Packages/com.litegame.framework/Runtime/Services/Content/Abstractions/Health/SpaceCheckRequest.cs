namespace LiteFramework
{
    /// <summary>
    /// 空间预检请求。调用方给出各分项，本类型只做**可测的算术与判定**。
    /// </summary>
    public sealed class SpaceCheckRequest
    {
        /// <summary>候选文件总字节（= 清单 <see cref="ReleaseFileEntry.Length"/> 之和）。</summary>
        public long CandidateBytes;

        /// <summary>解压峰值（未压缩包为 0）。</summary>
        public long DecompressPeakBytes;

        /// <summary>保留版本的既有占用（不得为其腾挪而破坏可用/恢复版本）。</summary>
        public long RetainedVersionBytes;

        /// <summary>安全余量（平台策略给值；不虚构固定值）。</summary>
        public long SafetyMarginBytes;
    }
}
