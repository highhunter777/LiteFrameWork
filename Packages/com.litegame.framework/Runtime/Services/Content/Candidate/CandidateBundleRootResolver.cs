using System;
using LiteFramework;

namespace LiteFramework
{
    /// <summary>
    /// 候选资产包根判定（《热更与内容发布专项设计》§7/§8）：资源包初始化时决定"是否以**已确认候选**的 资产包根启动"。
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