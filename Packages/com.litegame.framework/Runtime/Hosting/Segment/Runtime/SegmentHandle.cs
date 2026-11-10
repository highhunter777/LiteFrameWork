using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteFramework
{
    /// <summary>模块关闭句柄（初始化成功后创建；把"成功"与"可关闭"绑成一个对象）。</summary>
    internal sealed class SegmentHandle : IClientSegmentHandle
    {
        private readonly IClientSegment _module;

        public SegmentHandle(IClientSegment module) => _module = module;

        public UniTask ShutdownAsync(CancellationToken ct) => _module.ShutdownAsync(ct);

        public override string ToString() => "SegmentHandle(" + _module.Name + ")";
    }
}
