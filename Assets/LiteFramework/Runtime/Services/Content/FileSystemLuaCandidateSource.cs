using System;
using System.Collections.Generic;
using LiteFramework;

namespace LiteFramework
{
    /// <summary>
    /// ICandidateLuaSource 的文件系统实现：候选根 lua/ 全量脚本 → require 缓存键字节。
    ///
    /// 代次由装配点委托注入（生产 = () =&gt; activation.ActiveGeneration）；未确认/内置 → IsActive=false。
    /// 只遍历候选目录，不读 YooAsset；是否整套由发布侧全量暂存 + LuaScriptsHealthProbe 保证。
    /// </summary>
    public sealed class FileSystemLuaCandidateSource : ICandidateLuaSource
    {
        private readonly IFileStore _files;
        private readonly Func<ContentGeneration> _generation;

        public FileSystemLuaCandidateSource(IFileStore files, Func<ContentGeneration> generation)
        {
            _files = files ?? throw new ArgumentNullException(nameof(files));
            _generation = generation ?? throw new ArgumentNullException(nameof(generation));
        }

        public bool IsActive
        {
            get
            {
                ContentGeneration generation = _generation();
                if (string.Equals(generation.ReleaseId, ContentGeneration.BuiltinReleaseId, StringComparison.Ordinal))
                    return false;
                return _files.DirectoryExists(LuaDirectory);
            }
        }

        public bool TryLoadAll(out IReadOnlyDictionary<string, byte[]> scripts, out string problem)
        {
            scripts = null;
            problem = null;
            if (!IsActive)
            {
                problem = "无已确认候选的 lua/ 段";
                return false;
            }

            string[] files = _files.GetFilesRecursive(LuaDirectory);
            var cache = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (string path in files)
            {
                if (!path.EndsWith(".lua", StringComparison.Ordinal)) continue;
                byte[] bytes = _files.ReadAllBytes(path);
                if (bytes == null || bytes.Length == 0)
                {
                    problem = "候选 Lua 空文件：" + path;
                    return false;
                }

                string key = path.Substring(LuaDirectory.Length + 1, path.Length - LuaDirectory.Length - 5);
                if (cache.ContainsKey(key))
                {
                    problem = "候选 Lua 重复 require 键：" + key;
                    return false;
                }
                cache[key] = bytes;
            }

            if (cache.Count == 0)
            {
                problem = "候选 lua/ 段为空";
                return false;
            }

            scripts = cache;
            return true;
        }

        private static string LuaDirectory
            => ReleaseLayout.CandidateRootRelative + "/" + ReleaseLayout.LuaPrefix.TrimEnd('/');
    }
}
