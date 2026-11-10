using System;
using System.Collections.Generic;
using LiteFramework;

namespace LiteGame.Modules.Config
{
    /// <summary>
    /// ICandidateConfigSource 的文件系统实现：候选根 config/ 段 → 全量表字节。
    /// </summary>
    public sealed class CandidateConfigSource : ICandidateConfigSource
    {
        private readonly IFileStore _files;
        private readonly Func<ContentGeneration> _generation;

        public CandidateConfigSource(IFileStore files, Func<ContentGeneration> generation)
        {
            _files = files ?? throw new ArgumentNullException(nameof(files));
            _generation = generation ?? throw new ArgumentNullException(nameof(generation));
        }

        public bool HasConfigSegment
        {
            get
            {
                ContentGeneration generation = _generation();
                if (string.Equals(generation.ReleaseId, ContentGeneration.BuiltinReleaseId, StringComparison.Ordinal))
                    return false;
                return HasConfigDirectory(_files, ReleaseLayout.CandidateRootRelative);
            }
        }

        public bool TryReadAll(out IReadOnlyDictionary<string, byte[]> tables, out string problem)
        {
            if (!HasConfigSegment)
            {
                tables = null;
                problem = "无已确认候选的 config/ 段";
                return false;
            }
            return TryReadAllAt(_files, ReleaseLayout.CandidateRootRelative, out tables, out problem);
        }

        /// <summary>候选根 config/ 是否存在（健康探针在确认前使用——不依赖激活代次）。</summary>
        public static bool HasConfigDirectory(IFileStore files, string candidateRoot)
            => files != null && !string.IsNullOrEmpty(candidateRoot)
               && files.DirectoryExists(candidateRoot.TrimEnd('/') + "/" + ReleaseLayout.ConfigPrefix.TrimEnd('/'));

        /// <summary>从指定候选根读全量表字节（健康探针用；不做代次判断）。</summary>
        public static bool TryReadAllAt(IFileStore files, string candidateRoot,
            out IReadOnlyDictionary<string, byte[]> tables, out string problem)
        {
            if (files == null) throw new ArgumentNullException(nameof(files));
            string root = ReleaseLayout.RequireCandidateRoot(candidateRoot, nameof(candidateRoot));

            var cache = new Dictionary<string, byte[]>(TableLayout.Files.Length, StringComparer.Ordinal);
            foreach (string file in TableLayout.Files)
            {
                string rel = root + "/" + ReleaseLayout.ConfigPrefix + file + ".bytes";
                byte[] bytes = files.ReadAllBytes(rel);
                if (bytes == null || bytes.Length == 0)
                {
                    tables = null;
                    problem = "候选 config 缺表或空表：" + rel;
                    return false;
                }
                cache[TableLayout.TableName(file)] = bytes;
            }

            tables = cache;
            problem = null;
            return true;
        }
    }
}
