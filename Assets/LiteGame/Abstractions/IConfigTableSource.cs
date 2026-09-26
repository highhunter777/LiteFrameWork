using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteGame
{
    /// <summary>
    /// 配置表来源（**通用，零 Luban 名**——《商级通用客户端框架总设计》§5 目标架构：
    /// `Serialization.Luban` 是 Adapter，运行时/流程只依赖抽象）。
    ///
    /// **为什么要有这一层**：`Luban.Tables` 是**生成的**类型（名字来自表源），不是运行时单元。
    /// 若流程直接依赖它，会出现两个后果：① 运行时程序集必须引用 Luban 生成程序集；
    /// ② 表源加一张表就可能牵动运行时。
    /// 故把"能取到表"抽象成接口，`LoadAsync` 也收在这里——消费方不需要知道表是怎么建出来的。
    /// </summary>
    public interface IConfigTableSource
    {
        bool Loaded { get; }
        UniTask LoadAsync(CancellationToken ct);   // 候选构建→校验→原子发布；完成即放行——签名对齐 §7.7
    }
}
