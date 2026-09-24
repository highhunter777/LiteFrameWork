using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteFramework
{
    /// <summary>
    /// 单项健康探针（《热更与内容发布专项设计》§8"健康确认至少覆盖候选 ConfigSnapshot、Lua/main、
    /// 全部必需注册表、关键 UI/入口及其资源"）。
    ///
    /// **一项一个探针**（而非一个万能探针）：设计逐项列举了覆盖范围，
    /// 合并成一个"全都检查"的接口会让失败无法定位到具体项。
    /// </summary>
    public interface IHealthProbe
    {
        /// <summary>探针名（诊断/失败定位；不含凭据）。</summary>
        string Name { get; }

        /// <summary>检查候选；返回 null/空 = 健康，否则为失败说明。</summary>
        UniTask<string> CheckAsync(ReleaseManifest candidate, CancellationToken ct = default);
    }

    /// <summary>
    /// 健康确认聚合（§8）。逐项执行**全部**探针——不短路：
    /// 一次报告全部不健康项，避免"修一个报一个"的反复试错（与 <see cref="MetaConfig"/> 校验同款纪律）。
    ///
    /// 本类实现 <see cref="ICandidateHealthCheck"/>，直接供 <see cref="PatchCoordinator"/> 使用。
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
        /// 逐项检查。返回 null = 全部健康；否则为**全部失败项的合并说明**。
        ///
        /// 探针自身抛异常按"该项不健康"处理——健康检查不应因某个探针实现缺陷而中断整条链路，
        /// 更不应把异常当成"健康"。
        /// </summary>
        public async UniTask<string> CheckAsync(ReleaseManifest candidate, CancellationToken ct = default)
        {
            if (_probes.Count == 0)
                return "无健康探针——不得在未确认任何覆盖项的情况下声称健康（§8）";

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
                    // 探针缺陷按不健康处理，且带上类型便于定位
                    reason = $"{probe.Name}：探针异常 {ex.GetType().Name}：{ex.Message}";
                }

                if (!string.IsNullOrEmpty(reason))
                    failures.Add($"{probe.Name}：{reason}");
            }

            return failures.Count == 0 ? null : string.Join(" | ", failures);
        }
    }
}
