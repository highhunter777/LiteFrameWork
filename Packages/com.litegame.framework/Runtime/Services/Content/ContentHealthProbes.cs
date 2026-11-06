using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using UnityEngine;

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

    /// <summary>
    /// 已发布资源的健康探针（§8"关键 UI/入口及其资源"）——**加载**而非仅存在性。
    /// </summary>
    public sealed class AssetsHealthProbe : IHealthProbe
    {
        private readonly IContentService _content;
        private readonly IReadOnlyList<string> _locations;

        public string Name => "entry-assets";

        /// <summary>最近一次检查中**属于当前代次**并实际加载校验的 location 数（证据行核对用）。</summary>
        public int LastChecked { get; private set; }

        /// <summary>最近一次检查中**不在当前代次清单内**而跳过的 location 数（证据行核对用）。</summary>
        public int LastSkipped { get; private set; }

        /// <param name="content">内容服务（与运行时同一条租约通道；实现 <see cref="IContentLocationCatalog"/> 时启用跳过判据）。</param>
        /// <param name="locations">关键入口资源的 location 列表（装配点注入）。</param>
        public AssetsHealthProbe(IContentService content, IReadOnlyList<string> locations)
        {
            _content = content ?? throw new ArgumentNullException(nameof(content));
            _locations = locations ?? Array.Empty<string>();
        }

        public async UniTask<string> CheckAsync(ReleaseManifest candidate, CancellationToken ct = default)
        {
            LastChecked = 0;
            LastSkipped = 0;
            if (_locations.Count == 0) return null;        // 未声明入口 = 本探针无覆盖项

            var catalog = _content as IContentLocationCatalog;
            var problems = new List<string>();
            foreach (string location in _locations)
            {
                if (ct.IsCancellationRequested) return "入口资源检查已取消";

                // 不属于当前代次 = 跳过（"内置代次没有候选专属入口"不是失败；属于但加载不出才是）。
                if (catalog != null && !catalog.IsLocationValid(location))
                {
                    LastSkipped++;
                    continue;
                }
                LastChecked++;
                try
                {
                    // 走真实加载路径；取到即释放（不计入长期持有）。
                    using (AssetLease<GameObject> lease = await _content.AcquireAsync<GameObject>(location, default, ct))
                    {
                        if (lease == null || lease.Asset == null) problems.Add(location + "：取到空资产");
                    }
                }
                catch (OperationCanceledException)
                {
                    return "入口资源检查已取消";
                }
                catch (Exception ex)
                {
                    problems.Add($"{location}：{ex.GetType().Name}");
                }
            }
            return problems.Count == 0 ? null : "入口资源不可加载：" + string.Join(", ", problems);
        }
    }
}
