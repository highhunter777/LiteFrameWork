using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteFramework
{
    /// <summary>
    /// 客户端模块契约（《商业级通用客户端框架总设计》§6.1）：ClientHost 按依赖顺序初始化/逆序关闭的最小单元。
    /// 模块 = 一块有独立生命周期的启动能力（平台设施、设置、Lua 宿主、UI 壳、表现壳……）；
    /// "依赖顺序即注册顺序"由装配方（GameEntry 收敛后的引导适配器）表达，Host 不做依赖图推断。
    /// </summary>
    public interface IClientModule
    {
        /// <summary>模块名（诊断/日志/测试断言用；Host 不依赖唯一性做键）。</summary>
        string Name { get; }

        /// <summary>初始化。契约：ct 取消必须以 OperationCanceledException 形态穿透（不吞、不换装）；
        /// 失败抛出即触发 Host 的"只关闭已成功模块"回滚路径。</summary>
        UniTask InitializeAsync(ClientContext context, CancellationToken ct);

        /// <summary>逆序关闭。契约：即使从未初始化成功也必须可安全调用（Host 只对已成功者调用，但模块自身要防重入）；
        /// 不得抛出（异常由调用方兜底聚合——关闭路径上"漏关一个"比"抛一个"代价大得多）；ct 用于有界等待而非抢跑。</summary>
        UniTask ShutdownAsync(CancellationToken ct);
    }

    /// <summary>
    /// 启动上下文：Host 建立的公共设施 + 模块间产物传递面。
    /// 设计取舍：**不用字典做服务定位**（§4 原则 8 反对静态定位器的理由同样适用于实例定位器）——
    /// 模块产物按类型写入/读取，装配关系在编译期可见；冲突写入即抛（同类型两个产物 = 装配错误，显性失败）。
    /// </summary>
    public sealed class ClientContext
    {
        private readonly object _gate = new object();
        private readonly Dictionary<Type, object> _byType = new Dictionary<Type, object>();

        /// <summary>Host 持有的根作用域（根取消源；模块登记长生命周期资源于此）。</summary>
        public ClientScope RootScope { get; }

        public ClientContext(ClientScope rootScope)
        {
            RootScope = rootScope ?? throw new ArgumentNullException(nameof(rootScope));
        }

        /// <summary>按类型登记模块产物（同类型重复登记即装配错误）。</summary>
        public void Put<T>(T product) where T : class
        {
            lock (_gate)
            {
                if (_byType.TryGetValue(typeof(T), out object existing) && !ReferenceEquals(existing, product))
                    throw new InvalidOperationException($"ClientContext 重复登记类型 {typeof(T).Name}（装配错误：同型产物只能有一个）");
                _byType[typeof(T)] = product;
            }
        }

        /// <summary>按类型取产物；缺失返回 null（缺失 = 依赖顺序错误或未装配，由消费方决定 fail-fast 语义）。</summary>
        public T Get<T>() where T : class
        {
            lock (_gate)
            {
                return _byType.TryGetValue(typeof(T), out object v) ? (T)v : null;
            }
        }

        /// <summary>按类型取产物；缺失即抛（fail-fast 依赖声明——比返回 null 后再 NRE 更可定位）。</summary>
        public T Require<T>() where T : class
        {
            T v = Get<T>();
            if (v == null) throw new InvalidOperationException($"ClientContext 缺少产物 {typeof(T).Name}（依赖顺序错误或未装配）");
            return v;
        }
    }
}
