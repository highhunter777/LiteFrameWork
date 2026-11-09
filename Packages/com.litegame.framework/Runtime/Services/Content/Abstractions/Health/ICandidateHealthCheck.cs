using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteFramework
{
    /// <summary>
    /// 候选内容健康确认端口：健康确认至少覆盖候选 ConfigSnapshot、程序集、全量表、关键 UI/入口及其资源。
    /// </summary>
    public interface ICandidateHealthCheck
    {
        /// <summary>对**已校验通过**的候选做健康确认；返回失败说明（null/空 = 健康）。</summary>
        UniTask<string> CheckAsync(ReleaseManifest candidate, CancellationToken ct = default);
    }
}
