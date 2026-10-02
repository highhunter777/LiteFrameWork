using System;
using LiteFramework;

namespace LiteGame
{
    /// <summary>
    /// 候选资产包根判定（《热更与内容发布专项设计》§7/§8）：资源包初始化时决定"是否以**已确认候选**的
    /// 资产包根启动"。
    ///
    /// 判据（两条齐备才用候选根，任一不满足即退回内置包根——fail-safe）：
    /// ① 激活记录的已确认发布身份**不是内置**（<see cref="ContentGeneration.Default"/> 的 ReleaseId）；
    /// ② 候选资产包**版本文件已就位**（`&lt;候选&gt;/bundle/&lt;包名&gt;.version`）——
    ///    它是 YooAsset 内置文件系统初始化最先应答的文件，缺失即包不完整；退回内置比让初始化裸失败更安全
    ///    （错误态由既有流程呈现）。
    ///
    /// 纯规则（相对路径存在性经 <paramref name="fileExists"/> 注入）——零 IO 依赖，可单测。
    /// </summary>
    public static class CandidateBundleRootResolver
    {
        /// <summary>YooAsset 内置包版本文件名（包名 + ".version"——与 BuiltinFileSystem 版本应答同约定）。</summary>
        public static string VersionFileName(string packageName)
            => (string.IsNullOrEmpty(packageName) ? "DefaultPackage" : packageName) + ".version";

        /// <summary>
        /// 是否以已确认候选的资产包根初始化（false = 沿用内置包根）。
        /// </summary>
        /// <param name="confirmedReleaseId">激活记录的已确认发布身份（null/空/内置 = 无候选）。</param>
        /// <param name="candidateBundleRootRelative">候选资产包根相对路径（= <see cref="ReleaseLayout.CandidateBundleRootRelative"/>）。</param>
        /// <param name="packageName">YooAsset 包名（版本文件名由此派生）。</param>
        /// <param name="fileExists">相对路径存在性探测（注入以便零 IO 单测）。</param>
        public static bool IsCandidateBundleReady(string confirmedReleaseId, string candidateBundleRootRelative,
            string packageName, Func<string, bool> fileExists)
        {
            if (fileExists == null) throw new ArgumentNullException(nameof(fileExists));
            if (string.IsNullOrEmpty(confirmedReleaseId)) return false;
            if (string.Equals(confirmedReleaseId, ContentGeneration.Default.ReleaseId, StringComparison.Ordinal))
                return false;
            if (string.IsNullOrEmpty(candidateBundleRootRelative)) return false;

            return fileExists(candidateBundleRootRelative + "/" + VersionFileName(packageName));
        }
    }
}