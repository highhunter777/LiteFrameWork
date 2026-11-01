using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteFramework
{
    /// <summary>
    /// 单项健康探针：健康确认至少覆盖候选 ConfigSnapshot、Lua/main、
    /// 全部必需注册表、关键 UI/入口及其资源。
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
}
