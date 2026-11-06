using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteFramework
{
    /// <summary>
    /// Lua 脚本健康探针（《热更与内容发布专项设计》§8"健康确认至少覆盖候选 …Lua/main…"； §10 候选验证）。
    /// </summary>
    public sealed class LuaScriptsHealthProbe : IHealthProbe
    {
        private readonly string _candidateRoot;
        private readonly Func<ReleaseManifest, IReadOnlyList<ScriptEntry>> _scriptMap;
        private readonly IFileStore _files;

        public string Name => "lua-scripts";

        /// <param name="candidateRoot">候选根（IFileStore 相对路径）。</param>
        /// <param name="scriptMap">清单 → 脚本条目映射（装配点注入）。</param>
        public LuaScriptsHealthProbe(string candidateRoot,
            Func<ReleaseManifest, IReadOnlyList<ScriptEntry>> scriptMap, IFileStore files = null)
        {
            _candidateRoot = string.IsNullOrEmpty(candidateRoot)
                ? throw new ArgumentException("候选根不能为空", nameof(candidateRoot))
                : candidateRoot.TrimEnd('/');
            _scriptMap = scriptMap ?? throw new ArgumentNullException(nameof(scriptMap));
            _files = files ?? throw new System.ArgumentNullException(nameof(files), "IFileStore 未注入");
        }

        public UniTask<string> CheckAsync(ReleaseManifest candidate, CancellationToken ct = default)
        {
            if (candidate == null) return UniTask.FromResult("候选为空");

            IReadOnlyList<ScriptEntry> entries = _scriptMap(candidate);
            if (entries == null || entries.Count == 0)
                return UniTask.FromResult<string>(null);     // 本发布无 Lua 脚本 = 无需检查（不是失败）

            if (!CandidateScriptSet.TryBuild(entries, out CandidateScriptSet set).Accepted)
                return UniTask.FromResult("脚本集合非法：" + CandidateScriptSet.TryBuild(entries, out _).Reason);

            // 读字节（走 FileSystem——LiteGame 层零 System.IO）。
            var bytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (ScriptEntry e in set.Entries)
            {
                if (ct.IsCancellationRequested) return UniTask.FromResult("Lua 健康检查已取消");

                byte[] b = _files.ReadAllBytes(_candidateRoot + "/" + e.Path);
                if (b == null) return UniTask.FromResult($"{e.Module}：候选根下读不到 {e.Path}");
                bytes[e.Path] = b;
            }

            using (var validator = new CandidateLuaValidator())
            {
                IReadOnlyList<LuaScriptVerdict> verdicts = validator.Validate(set, bytes);
                return UniTask.FromResult(CandidateLuaValidator.Summarize(verdicts));
            }
        }
    }
}
