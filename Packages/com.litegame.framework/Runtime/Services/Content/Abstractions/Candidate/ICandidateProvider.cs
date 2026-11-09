using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteFramework
{
    /// <summary>
    /// 候选来源端口。
    /// </summary>
    public interface ICandidateProvider
    {
        /// <summary>取候选；无候选返回 <see cref="CandidateOffer.IsEmpty"/> 为 true 的结果（不是异常）。</summary>
        UniTask<CandidateOffer> TryGetCandidateAsync(CancellationToken ct = default);
    }
}
