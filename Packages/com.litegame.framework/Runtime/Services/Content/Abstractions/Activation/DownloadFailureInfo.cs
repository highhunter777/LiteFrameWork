namespace LiteFramework
{
    /// <summary>失败详情（稳定种类 + 可诊断路径/期望值——不含 token/密钥，日志红线）。</summary>
    public readonly struct DownloadFailureInfo
    {
        public readonly DownloadFailureKind Kind;
        public readonly string Path;
        public readonly string Expected;
        public readonly string Actual;
        public readonly string Detail;

        public DownloadFailureInfo(DownloadFailureKind kind, string path = null,
            string expected = null, string actual = null, string detail = null)
        {
            Kind = kind;
            Path = path;
            Expected = expected;
            Actual = actual;
            Detail = detail;
        }

        /// <summary>是否暂态（可重试/切源）。**确定性失败重试无效**——见枚举注释。</summary>
        public bool IsTransient
            => Kind == DownloadFailureKind.TransientNetwork || Kind == DownloadFailureKind.SourceUnavailable;

        public override string ToString()
        {
            var s = Kind.ToString();
            if (!string.IsNullOrEmpty(Path)) s += " " + Path;
            if (!string.IsNullOrEmpty(Expected) || !string.IsNullOrEmpty(Actual))
                s += $"（期望 {Expected ?? "-"} / 实得 {Actual ?? "-"}）";
            if (!string.IsNullOrEmpty(Detail)) s += "：" + Detail;
            return s;
        }
    }
}
