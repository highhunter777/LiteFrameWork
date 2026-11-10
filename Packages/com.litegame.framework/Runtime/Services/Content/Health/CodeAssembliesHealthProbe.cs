using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteFramework
{
    /// <summary>候选程序集健康探针：形状与可读性；摘要与签名由发布清单/验签负责。</summary>
    public sealed class CodeAssembliesHealthProbe : IHealthProbe
    {
        private readonly string _candidateRoot;
        private readonly Func<ReleaseManifest, IReadOnlyList<CodeEntry>> _map;
        private readonly IFileStore _files;

        public string Name => "code-assemblies";

        public CodeAssembliesHealthProbe(string candidateRoot,
            Func<ReleaseManifest, IReadOnlyList<CodeEntry>> codeMap, IFileStore files)
        {
            _candidateRoot = ReleaseLayout.RequireCandidateRoot(candidateRoot, nameof(candidateRoot));
            _map = codeMap ?? throw new ArgumentNullException(nameof(codeMap));
            _files = files ?? throw new ArgumentNullException(nameof(files));
        }

        public UniTask<string> CheckAsync(ReleaseManifest candidate, CancellationToken ct = default)
        {
            if (candidate == null) return UniTask.FromResult("候选为空");
            IReadOnlyList<CodeEntry> entries = _map(candidate);
            if (entries == null || entries.Count == 0) return UniTask.FromResult<string>(null);
            foreach (CodeEntry entry in entries)
            {
                if (ct.IsCancellationRequested) return UniTask.FromResult("程序集健康检查已取消");
                byte[] bytes = _files.ReadAllBytes(_candidateRoot + "/" + entry.Path);
                if (bytes == null || bytes.Length < 2)
                    return UniTask.FromResult($"{entry.Name}：候选根下读不到或空文件（{entry.Path}）");
                if (bytes[0] != (byte)'M' || bytes[1] != (byte)'Z')
                    return UniTask.FromResult($"{entry.Name}：不是 PE 程序集（{entry.Path}）");
            }
            return UniTask.FromResult<string>(null);
        }
    }
}
