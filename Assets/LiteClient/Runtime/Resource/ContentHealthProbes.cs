using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// 候选配置健康探针（《热更与内容发布专项设计》§8"健康确认至少覆盖候选 ConfigSnapshot…"；
    /// §11"候选先解析与验证，再构造不可变 Snapshot"）。
    ///
    /// **核验的是"候选文件本身可解析"，不是"已切换成候选"**——健康确认发生在激活之后，
    /// 但探针应能对**指定清单**独立作答，否则无法在激活前预检、也无法区分
    /// "候选坏"与"激活过程坏"。
    ///
    /// 本探针做的检查（§11 的"schema、外键、资源键"中**可在此层做**的部分）：
    /// ① 清单声明的配置项**全部存在且非空**；② JSON **可解析**（不是截断/畸形）；
    /// ③ 结构化产物**根对象形态正确**（数组/对象之一，非裸标量）。
    /// 更深的表间外键与数值域校验依赖 Luban 生成的 schema，不在本探针覆盖范围。
    /// </summary>
    public sealed class CandidateConfigHealthProbe : IHealthProbe
    {
        private readonly string _candidateRoot;
        private readonly Func<ReleaseManifest, IReadOnlyList<string>> _configPaths;

        public string Name => "candidate-config";

        /// <param name="candidateRoot">候选根（FileSys 相对路径）。</param>
        /// <param name="configPaths">清单 → 配置文件的相对路径集合（装配点注入——哪些文件是配置属发布约定）。</param>
        public CandidateConfigHealthProbe(string candidateRoot,
            Func<ReleaseManifest, IReadOnlyList<string>> configPaths)
        {
            _candidateRoot = string.IsNullOrEmpty(candidateRoot)
                ? throw new ArgumentException("候选根不能为空", nameof(candidateRoot))
                : candidateRoot.TrimEnd('/');
            _configPaths = configPaths ?? throw new ArgumentNullException(nameof(configPaths));
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

                byte[] bytes = FileSys.ReadAllBytes(_candidateRoot + "/" + rel);
                if (bytes == null || bytes.Length == 0)
                {
                    problems.Add($"{rel}：缺失或为空");
                    continue;
                }

                string json = System.Text.Encoding.UTF8.GetString(bytes);
                string shape = CheckShape(json);
                if (shape != null) problems.Add($"{rel}：{shape}");
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
    /// 关键入口资源探针（§8"健康确认至少覆盖…关键 UI/入口及其资源"）。
    ///
    /// **本探针只做候选根内的文件存在性检查**，不加载 prefab——
    /// 加载需要 YooAsset 包与真实资产管线，且"加载成功"是激活**之后**才有意义的事实。
    /// 这里回答的是"候选里到底有没有那几个入口文件"，
    /// 真实可加载性由 <see cref="AssetsHealthProbe"/>（走 AssetDatabase）承担。
    /// </summary>
    public sealed class CandidateEntryFilesHealthProbe : IHealthProbe
    {
        private readonly string _candidateRoot;
        private readonly Func<ReleaseManifest, IReadOnlyList<string>> _entryPaths;

        public string Name => "candidate-entry-files";

        public CandidateEntryFilesHealthProbe(string candidateRoot,
            Func<ReleaseManifest, IReadOnlyList<string>> entryPaths)
        {
            _candidateRoot = string.IsNullOrEmpty(candidateRoot)
                ? throw new ArgumentException("候选根不能为空", nameof(candidateRoot))
                : candidateRoot.TrimEnd('/');
            _entryPaths = entryPaths ?? throw new ArgumentNullException(nameof(entryPaths));
        }

        public UniTask<string> CheckAsync(ReleaseManifest candidate, CancellationToken ct = default)
        {
            if (candidate == null) return UniTask.FromResult("候选为空");

            IReadOnlyList<string> paths = _entryPaths(candidate);
            if (paths == null || paths.Count == 0) return UniTask.FromResult<string>(null);

            var problems = new List<string>();
            foreach (string rel in paths)
            {
                if (ct.IsCancellationRequested) return UniTask.FromResult("入口资源检查已取消");
                if (FileSys.GetFileLength(_candidateRoot + "/" + rel) <= 0)
                    problems.Add(rel);
            }
            return UniTask.FromResult(problems.Count == 0
                ? null
                : "入口资源缺失或为空：" + string.Join(", ", problems));
        }
    }

    /// <summary>
    /// 已发布资源的健康探针（§8"关键 UI/入口及其资源"）——**加载**而非仅存在性。
    ///
    /// 经 <see cref="IContentService"/> 租约通道获取并立即释放：走的是与运行时**同一条**加载路径，
    /// 因此"能取到"本身就是有效证据；若走 AssetDatabase 直读则测不出运行时才会暴露的
    /// 收集组/依赖/类型问题。
    ///
    /// **作用范围如实标注**：检查的是**当前代次**（= 已确认版本）的入口资源，
    /// **不是候选内容**（候选尚未切换代次）。它验证"入口资源在真实加载路径上可用"，
    /// 用于捕捉"更新后入口坏了"这类回归；候选专属的资源可加载性需激活后由健康确认覆盖。
    /// </summary>
    public sealed class AssetsHealthProbe : IHealthProbe
    {
        private readonly IContentService _content;
        private readonly IReadOnlyList<string> _locations;

        public string Name => "entry-assets";

        /// <param name="content">内容服务（与运行时同一条租约通道）。</param>
        /// <param name="locations">关键入口资源的 location 列表（装配点注入）。</param>
        public AssetsHealthProbe(IContentService content, IReadOnlyList<string> locations)
        {
            _content = content ?? throw new ArgumentNullException(nameof(content));
            _locations = locations ?? Array.Empty<string>();
        }

        public async UniTask<string> CheckAsync(ReleaseManifest candidate, CancellationToken ct = default)
        {
            if (_locations.Count == 0) return null;        // 未声明入口 = 本探针无覆盖项

            var problems = new List<string>();
            foreach (string location in _locations)
            {
                if (ct.IsCancellationRequested) return "入口资源检查已取消";
                try
                {
                    // 走真实加载路径；取到即释放（不计入长期持有）
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
