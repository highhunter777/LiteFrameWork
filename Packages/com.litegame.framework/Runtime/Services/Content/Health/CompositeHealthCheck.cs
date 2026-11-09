using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteFramework
{

    /// <summary>
    /// 健康确认聚合。
    /// </summary>
    public sealed class CompositeHealthCheck : ICandidateHealthCheck
    {
        private readonly List<IHealthProbe> _probes = new List<IHealthProbe>();

        /// <summary>已登记探针数（诊断/装配断言）。</summary>
        public int ProbeCount => _probes.Count;

        public CompositeHealthCheck(params IHealthProbe[] probes)
        {
            if (probes != null)
            {
                foreach (IHealthProbe p in probes)
                    if (p != null) _probes.Add(p);
            }
        }

        /// <summary>追加探针（装配期；返回自身便于链式）。</summary>
        public CompositeHealthCheck Add(IHealthProbe probe)
        {
            if (probe != null) _probes.Add(probe);
            return this;
        }

        /// <summary>
        /// 逐项检查。
        /// </summary>
        public async UniTask<string> CheckAsync(ReleaseManifest candidate, CancellationToken ct = default)
        {
            if (_probes.Count == 0)
                return "无健康探针——不得在未确认任何覆盖项的情况下声称健康";

            var failures = new List<string>();
            foreach (IHealthProbe probe in _probes)
            {
                if (ct.IsCancellationRequested) return "健康检查已取消";

                string reason;
                try
                {
                    reason = await probe.CheckAsync(candidate, ct);
                }
                catch (OperationCanceledException)
                {
                    return "健康检查已取消";
                }
                catch (Exception ex)
                {
                    // 探针缺陷按不健康处理，且带上类型便于定位。
                    reason = $"{probe.Name}：探针异常 {ex.GetType().Name}：{ex.Message}";
                }

                if (!string.IsNullOrEmpty(reason))
                    failures.Add($"{probe.Name}：{reason}");
            }

            return failures.Count == 0 ? null : string.Join(" | ", failures);
        }
    }
}
