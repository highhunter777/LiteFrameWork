using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace LiteFramework
{
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
