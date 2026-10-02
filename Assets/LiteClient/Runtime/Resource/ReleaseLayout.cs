using System.Collections.Generic;
using LiteFramework;

namespace LiteClient
{
    /// <summary>
    /// 发布布局约定（《热更与内容发布专项设计》§7/§8 的单源——探针装配与候选校验共用，
    /// "哪些清单文件是配置/脚本/资产包"属发布约定，不散写）。
    ///
    /// 候选根布局：`config/**`（Luban 表字节）+ `lua/**.lua`（热更脚本）+ `bundle/**`
    /// （**资产包根镜像**：YooAsset 包根的版本/清单/Bundle 文件，`bundle/` 本身即一个包根）。
    /// Lua 模块名派生与运行期 <c>LuaPreloader</c> 同规则（剥前缀与 .lua 后缀、'/' 保留），
    /// 保证候选沙箱内 require 的解析空间与正式包一致。
    /// </summary>
    public static class ReleaseLayout
    {
        /// <summary>配置文件前缀（候选根相对路径）。</summary>
        public const string ConfigPrefix = "config/";

        /// <summary>热更脚本前缀（候选根相对路径）。</summary>
        public const string LuaPrefix = "lua/";

        /// <summary>资产包前缀（候选根相对路径；前缀下即一个 YooAsset 包根镜像）。</summary>
        public const string BundlePrefix = "bundle/";

        /// <summary>候选根（FileSys 相对路径；装配与发布脚本共用单源）。</summary>
        public const string CandidateRootRelative = "content/candidate";

        /// <summary>候选根内的资产包根相对路径（= <see cref="BundlePrefix"/> 去尾斜杠——YooAsset 包根用之）。</summary>
        public const string BundleRootRelative = "bundle";

        /// <summary>候选资产包根（FileSys 相对路径；资源层解析根与存在性探测共用单源）。</summary>
        public const string CandidateBundleRootRelative = CandidateRootRelative + "/" + BundleRootRelative;

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

        /// <summary>清单 → 热更脚本条目（模块名派生：剥 lua/ 前缀与 .lua 后缀、'/' 保留）。</summary>
        public static IReadOnlyList<ScriptEntry> LuaScripts(ReleaseManifest manifest)
        {
            var entries = new List<ScriptEntry>();
            foreach (ReleaseFileEntry f in manifest.Files)
            {
                if (!f.Path.StartsWith(LuaPrefix, System.StringComparison.Ordinal)
                    || !f.Path.EndsWith(".lua", System.StringComparison.Ordinal)) continue;
                entries.Add(new ScriptEntry(LuaModuleOf(f.Path), f.Path));
            }
            return entries;
        }

        /// <summary>候选脚本路径 → Lua require 模块名（如 lua/ui/UIMain.lua → ui/UIMain）。</summary>
        public static string LuaModuleOf(string candidatePath)
        {
            if (string.IsNullOrEmpty(candidatePath)) return candidatePath;
            string p = candidatePath;
            if (p.StartsWith(LuaPrefix, System.StringComparison.Ordinal)) p = p.Substring(LuaPrefix.Length);
            if (p.EndsWith(".lua", System.StringComparison.Ordinal)) p = p.Substring(0, p.Length - 4);
            return p;
        }
    }
}
