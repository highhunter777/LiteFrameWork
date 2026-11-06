namespace LiteFramework
{
    /// <summary>
    /// 下载/校验失败的稳定分类（区分暂态网络错误与签名/兼容错误）。
    /// </summary>
    public enum DownloadFailureKind
    {
        None = 0,

        // ---- 暂态（可重试/可切源）----。
        /// <summary>网络超时/连接失败（暂态网络/CDN：有界重试/切源）。</summary>
        TransientNetwork,
        /// <summary>数据源不可用（单源故障；可切源）。</summary>
        SourceUnavailable,

        // ---- 确定性（重试无效）----。
        /// <summary>文件缺失（候选损坏/解析失败：隔离该候选、保留旧版本）。</summary>
        FileMissing,
        /// <summary>文件长度与清单不符。</summary>
        LengthMismatch,
        /// <summary>文件摘要与清单不符（内容损坏或被篡改）。</summary>
        DigestMismatch,
        /// <summary>读取过程异常（不可读/权限/损坏）。</summary>
        ReadError,
        /// <summary>空间不足（空间/内存不足：清无引用受控缓存或取消更新）。</summary>
        InsufficientSpace,
        /// <summary>候选根内出现清单未声明的文件（防混入——多源拼接/残留污染）。</summary>
        UnexpectedFile,
        /// <summary>清单内文件在候选根重复（大小写/规范化后同名——防"同名不同源"混装）。</summary>
        DuplicateFile,
        /// <summary>取消（用户/后台切换；非错误，但需确定终态）。</summary>
        Canceled,
    }
}
