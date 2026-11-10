using System.Collections.Generic;
using LiteFramework;

namespace LiteFramework
{
    /// <summary>
    /// 发布布局约定（《热更与内容发布专项设计》§7/§8 的单源——探针装配与候选校验共用， "哪些清单文件是配置/脚本/资产包"属发布约定，不散写）。
    /// </summary>
    public static class ReleaseLayout
    {
        /// <summary>配置文件前缀（候选根相对路径）。</summary>
        public const string ConfigPrefix = "config/";

        /// <summary>热更程序集前缀（候选根相对路径；HybridCLR hot dll）。</summary>
        public const string CodePrefix = "code/";

        /// <summary>资产包前缀（候选根相对路径；前缀下即一个 YooAsset 包根镜像）。</summary>
        public const string BundlePrefix = "bundle/";

        /// <summary>候选根（FileSystem 相对路径；装配与发布脚本共用单源）。</summary>
        public const string CandidateRootRelative = "content/candidate";

        /// <summary>候选根内的资产包根相对路径（= <see cref="BundlePrefix"/> 去尾斜杠——YooAsset 包根用之）。</summary>
        public const string BundleRootRelative = "bundle";

        /// <summary>候选资产包根（FileSystem 相对路径；资源层解析根与存在性探测共用单源）。</summary>
        public const string CandidateBundleRootRelative = CandidateRootRelative + "/" + BundleRootRelative;

        /// <summary>候选根守卫：空串抛 <see cref="System.ArgumentException"/>，否则去尾斜杠（各候选实现构造点共用）。</summary>
        /// <param name="value">候选根（IFileStore 相对路径）。</param>
        /// <param name="paramName">调用方参数名（<c>nameof(...)</c>）。</param>
        public static string RequireCandidateRoot(string value, string paramName)
            => string.IsNullOrEmpty(value)
                ? throw new System.ArgumentException("候选根不能为空", paramName)
                : value.TrimEnd('/');

        /// <summary>清单 → 配置文件相对路径集合（health 探针与发布流水线共用）。</summary>
        public static IReadOnlyList<string> ConfigPaths(ReleaseManifest manifest)
        {
            var list = new List<string>();
            foreach (ReleaseFileEntry f in manifest.Files)
                if (f.Path.StartsWith(ConfigPrefix, System.StringComparison.Ordinal))
                    list.Add(f.Path);
            return list;
        }

        /// <summary>清单 → 资产包文件相对路径集合（与 <see cref="ConfigPaths"/> 同形）。</summary>
        public static IReadOnlyList<string> BundleFiles(ReleaseManifest manifest)
        {
            var list = new List<string>();
            foreach (ReleaseFileEntry f in manifest.Files)
                if (f.Path.StartsWith(BundlePrefix, System.StringComparison.Ordinal))
                    list.Add(f.Path);
            return list;
        }

        /// <summary>清单 → 热更程序集条目（code/ 前缀下的 .dll；程序集名 = 文件名去后缀）。</summary>
        public static IReadOnlyList<CodeEntry> CodeAssemblies(ReleaseManifest manifest)
        {
            var list = new List<CodeEntry>();
            foreach (ReleaseFileEntry f in manifest.Files)
            {
                if (!f.Path.StartsWith(CodePrefix, System.StringComparison.Ordinal)
                    || !f.Path.EndsWith(".dll", System.StringComparison.Ordinal)) continue;
                list.Add(new CodeEntry(CodeAssemblyNameOf(f.Path), f.Path));
            }
            return list;
        }

        /// <summary>候选程序集路径 → 程序集名（如 code/LiteSim.dll → LiteSim）。</summary>
        public static string CodeAssemblyNameOf(string candidatePath)
        {
            if (string.IsNullOrEmpty(candidatePath)) return candidatePath;
            string p = candidatePath;
            if (p.StartsWith(CodePrefix, System.StringComparison.Ordinal)) p = p.Substring(CodePrefix.Length);
            int slash = p.LastIndexOf('/');
            if (slash >= 0) p = p.Substring(slash + 1);
            if (p.EndsWith(".dll", System.StringComparison.Ordinal)) p = p.Substring(0, p.Length - 4);
            return p;
        }
    }
}
