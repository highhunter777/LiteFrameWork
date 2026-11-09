using Xunit;

namespace LiteFramework.Tests
{
    /// <summary>
    /// 触碰 Core 全局可变静态（Log 环形缓冲）的测试类 必须加入本 Collection（测试开发方案 §6.5）——DisableParallelization 使其与其余 Collection 也不并行。
    /// </summary>
    [CollectionDefinition("CoreStatic", DisableParallelization = true)]
    public sealed class CoreStaticCollection { }
}
