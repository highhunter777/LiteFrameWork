using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteFramework
{
    /// <summary>
    /// 客户端模块契约：ClientHost 按依赖顺序初始化/逆序关闭的最小单元。
    /// 模块 = 一块有独立生命周期的启动能力（平台设施、设置、Lua 宿主、UI 服务装配、表现域……）；
    /// "依赖顺序即注册顺序"由装配方（GameEntry 收敛后的引导适配器）表达，Host 不做依赖图推断。
    /// </summary>
    public interface IClientModule
    {
        /// <summary>模块名（诊断/日志/测试断言用；Host 不依赖唯一性做键）。</summary>
        string Name { get; }

        /// <summary>初始化。契约：ct 取消必须以 OperationCanceledException 形态穿透（不吞、不换装）；
        /// 失败抛出即触发 Host 的"只关闭已成功模块"回滚路径。
        /// <paramref name="context"/> 是本模块专属的装配窗口——Put 只在该窗口内合法（路线图 §4.3）。</summary>
        UniTask InitializeAsync(ModuleContext context, CancellationToken ct);

        /// <summary>逆序关闭。契约：即使从未初始化成功也必须可安全调用（Host 只对已成功者调用，但模块自身要防重入）；
        /// 不得抛出（异常由调用方兜底聚合——关闭路径上"漏关一个"比"抛一个"代价大得多）；ct 用于有界等待而非抢跑。</summary>
        UniTask ShutdownAsync(CancellationToken ct);
    }
}
