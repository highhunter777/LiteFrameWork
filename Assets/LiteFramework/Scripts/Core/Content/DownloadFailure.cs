using System;
using System.Collections.Generic;

namespace LiteFramework
{
    /// <summary>
    /// 下载/校验失败的稳定分类（《热更与内容发布专项设计》§7"区分暂态网络错误与签名/兼容错误"、
    /// §12 错误类别表）。
    ///
    /// **分类决定动作**，不是一个笼统的"失败"：
    /// - 暂态类可重试/切源；确定性类重试无效，必须拒绝候选或提示更新（§12"不通过反复重试或忽略验证绕过"）。
    /// - 因此本枚举带 <see cref="DownloadFailureInfo.IsTransient"/> 判定，调用方据此选择重试策略。
    /// </summary>
    public enum DownloadFailureKind
    {
        None = 0,

        // ---- 暂态（可重试/可切源）----
        /// <summary>网络超时/连接失败（§12"暂态网络/CDN：有界重试/切源"）。</summary>
        TransientNetwork,
        /// <summary>数据源不可用（单源故障；可切源）。</summary>
        SourceUnavailable,

        // ---- 确定性（重试无效）----
        /// <summary>文件缺失（§12"候选损坏/解析失败：隔离该候选、保留旧版本"）。</summary>
        FileMissing,
        /// <summary>文件长度与清单不符。</summary>
        LengthMismatch,
        /// <summary>文件摘要与清单不符（内容损坏或被篡改）。</summary>
        DigestMismatch,
        /// <summary>读取过程异常（不可读/权限/损坏）。</summary>
        ReadError,
        /// <summary>空间不足（§12"空间/内存不足：清无引用受控缓存或取消更新"）。</summary>
        InsufficientSpace,
        /// <summary>候选根内出现清单未声明的文件（防混入——多源拼接/残留污染）。</summary>
        UnexpectedFile,
        /// <summary>清单内文件在候选根重复（大小写/规范化后同名——防"同名不同源"混装）。</summary>
        DuplicateFile,
        /// <summary>取消（用户/后台切换；非错误，但需确定终态）。</summary>
        Canceled,
    }

    /// <summary>失败详情（稳定种类 + 可诊断路径/期望值——不含 token/密钥，§13.1 日志红线）。</summary>
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
