using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteFramework
{
    /// <summary>
    /// 候选配置健康探针（《热更与内容发布专项设计》§8"健康确认至少覆盖候选 ConfigSnapshot…"； §11"候选先解析与验证，再构造不可变 Snapshot"）。
    /// </summary>
    public sealed class CandidateConfigHealthProbe : IHealthProbe
    {
        private readonly string _candidateRoot;
        private readonly Func<ReleaseManifest, IReadOnlyList<string>> _configPaths;
        private readonly IFileStore _files;

        public string Name => "candidate-config";

        /// <param name="candidateRoot">候选根（IFileStore 相对路径）。</param>
        /// <param name="configPaths">清单 → 配置文件的相对路径集合（装配点注入——哪些文件是配置属发布约定）。</param>
        public CandidateConfigHealthProbe(string candidateRoot,
            Func<ReleaseManifest, IReadOnlyList<string>> configPaths, IFileStore files = null)
        {
            _candidateRoot = string.IsNullOrEmpty(candidateRoot)
                ? throw new ArgumentException("候选根不能为空", nameof(candidateRoot))
                : candidateRoot.TrimEnd('/');
            _configPaths = configPaths ?? throw new ArgumentNullException(nameof(configPaths));
            _files = files ?? throw new System.ArgumentNullException(nameof(files), "IFileStore 未注入");
        }

        public UniTask<string> CheckAsync(ReleaseManifest candidate, CancellationToken ct = default)
        {
            if (candidate == null) return UniTask.FromResult("候选为空");

            IReadOnlyList<string> paths = _configPaths(candidate);
            if (paths == null || paths.Count == 0)
                return UniTask.FromResult<string>(null);     // 本发布无配置产物 = 无需检查

            var problems = new List<string>();
            foreach (string rel in paths)
            {
                if (ct.IsCancellationRequested) return UniTask.FromResult("配置健康检查已取消");

                byte[] bytes = _files.ReadAllBytes(_candidateRoot + "/" + rel);
                if (bytes == null || bytes.Length == 0)
                {
                    problems.Add($"{rel}：缺失或为空");
                    continue;
                }

                // JSON 产物做结构校验；其余格式（Luban 二进制表字节等）只保证存在且非空——
                // 反序列化校验归运行时加载器（ConfigService），探针不重复 JSON 解析假设。
                if (rel.EndsWith(".json", StringComparison.Ordinal))
                {
                    string json = System.Text.Encoding.UTF8.GetString(bytes);
                    string shape = CheckShape(json);
                    if (shape != null) problems.Add($"{rel}：{shape}");
                }
            }

            return UniTask.FromResult(problems.Count == 0 ? null : string.Join(" | ", problems));
        }

        /// <summary>可解析性 + 根形态。返回 null = 合格。</summary>
        internal static string CheckShape(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return "内容为空";

            try
            {
                var token = Newtonsoft.Json.Linq.JToken.Parse(json);
                if (token is Newtonsoft.Json.Linq.JArray || token is Newtonsoft.Json.Linq.JObject)
                    return null;
                return "根节点不是数组或对象（裸标量不是合法配置产物）";
            }
            catch (Exception ex)
            {
                return "解析失败：" + Shorten(ex.Message);
            }
        }

        private static string Shorten(string m)
        {
            if (string.IsNullOrEmpty(m)) return "";
            m = m.Replace('\n', ' ').Replace('\r', ' ');
            return m.Length <= 120 ? m : m.Substring(0, 120) + "…";
        }
    }
}
