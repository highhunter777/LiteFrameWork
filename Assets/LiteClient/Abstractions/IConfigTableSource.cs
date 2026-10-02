using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteGame
{
    /// <summary>
    /// 配置表来源（通用、零 Luban 名）：`Serialization.Luban` 是 Adapter，运行时/流程只依赖抽象。
    /// 把"能取到表"抽象成接口——`Luban.Tables` 是生成类型，直接依赖会让运行时程序集耦合生成物。
    /// </summary>
    public interface IConfigTableSource
    {
        bool Loaded { get; }
        UniTask LoadAsync(CancellationToken ct);   // 候选构建→校验→原子发布；完成即放行——签名对齐 §7.7
    }
}
