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

    /// <summary>
    /// 模块依赖图声明（可选能力，路线图 M1）：声明本模块依赖的**其它模块类型**。
    /// Host 在初始化前校验：依赖必须已注册（缺失 fail-fast）、依赖边不得成环、模块名不得重复。
    /// 未实现本接口的模块视为无声明依赖（仍按注册序初始化）。
    /// </summary>
    public interface IClientModuleGraph
    {
        /// <summary>依赖的模块类型（注册序之外的第二道显式声明；供 DAG 校验与可读性）。</summary>
        IReadOnlyList<Type> DependsOn { get; }
    }

    /// <summary>
    /// 模块关闭句柄（路线图 M1）：初始化成功后返回，Host 只经句柄关闭——把"初始化成功"与
    /// "可关闭"绑成一个对象，避免对从未初始化成功的模块调用关闭。
    /// </summary>
    public interface IClientModuleHandle
    {
        /// <summary>关闭本模块（幂等；不得抛出——异常由 Host 聚合）。</summary>
        UniTask ShutdownAsync(CancellationToken ct);
    }

    /// <summary>
    /// 模块作用域契约（路线图 §4.3）：模块把本次初始化创建的资源/订阅/租约登记到这里，
    /// 由 Host 在模块关闭（或启动失败的该模块回滚）时**逆序释放**。
    /// 每个模块一个作用域（Root 的子作用域，取消链接到根）——资源归属随模块走，不混进根作用域。
    /// </summary>
    public interface IModuleScope
    {
        /// <summary>作用域名（`Module:&lt;模块名&gt;`，诊断用）。</summary>
        string Name { get; }

        /// <summary>本模块的取消令牌（父级＝根作用域；宿主退出/根取消级联）。</summary>
        CancellationToken Token { get; }

        /// <summary>登记可释放资源（模块关闭时逆序释放；登记进已释放作用域即就地释放并抛）。</summary>
        void Register(IDisposable resource);
    }

    /// <summary>模块作用域实现：包一层根作用域的子 <see cref="ClientScope"/>（LIFO 释放 + 取消链接）。
    /// 每个模块一个实例；Host 持有并在关闭/回滚路径释放。</summary>
    internal sealed class ModuleScope : IModuleScope, IDisposable
    {
        private readonly ClientScope _scope;

        internal ModuleScope(ClientScope root, string moduleName)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            _scope = root.CreateChild("Module:" + moduleName);
        }

        public string Name => _scope.Name;
        public CancellationToken Token => _scope.Token;
        public int OwnedCount => _scope.OwnedCount;
        public IReadOnlyList<Exception> DisposeFailures => _scope.DisposeFailures;

        public void Register(IDisposable resource) => _scope.Register(resource);

        public void Dispose() => _scope.Dispose();
    }

    /// <summary>
    /// 模块装配窗口（路线图 §4.3）：<see cref="IClientModule.InitializeAsync"/> 唯一的登记面。
    /// 每个模块拿到自己的窗口——只有窗口开启期间允许 <see cref="Put{T}"/>；Host 在该模块初始化返回后
    /// 立即关窗（成功/失败/取消/异步续体晚到一律拒绝），全部模块成功后密封全局装配面。
    /// 运行期只读（<see cref="Require{T}"/>/<see cref="Get{T}"/>），不提供新增注册。
    /// </summary>
    public sealed class ModuleContext
    {
        private readonly ClientContext _context;
        private readonly string _moduleName;
        private int _open = 1;

        internal ModuleContext(ClientContext context, string moduleName, IModuleScope scope)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _moduleName = moduleName ?? throw new ArgumentNullException(nameof(moduleName));
            Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        }

        /// <summary>窗口所属模块名（诊断/异常消息用）。</summary>
        public string ModuleName => _moduleName;

        /// <summary>本模块的资源作用域（初始化期资源登记在这里，随模块关闭释放）。</summary>
        public IModuleScope Scope { get; }

        /// <summary>根容器（模块装配期登记服务用；与 <see cref="Put{T}"/> 同一来源）。</summary>
        public ServiceContainer Services => _context.Services;

        /// <summary>根作用域（模块登记长生命周期资源/取根令牌）。</summary>
        public ClientScope RootScope => _context.RootScope;

        /// <summary>装配期登记产物。窗口关闭后（本模块初始化已返回）一律抛出——登记不得跨模块/跨帧。</summary>
        public void Put<T>(T product) where T : class
        {
            if (Volatile.Read(ref _open) == 0)
                throw new InvalidOperationException(
                    $"模块 {_moduleName} 的装配窗口已关闭——Put 只允许在本模块 InitializeAsync 内（路线图 §4.3）");
            _context.Put(product);
        }

        /// <summary>按类型取产物；缺失返回 null（由消费方决定 fail-fast 语义）。</summary>
        public T Get<T>() where T : class => _context.Get<T>();

        /// <summary>按类型取产物；缺失即抛（fail-fast 依赖声明）。</summary>
        public T Require<T>() where T : class => _context.Require<T>();

        internal void Close() => Interlocked.Exchange(ref _open, 0);
    }

    /// <summary>
    /// 启动装配上下文：模块产物与 Root 服务容器共享同一来源。
    /// 同类型同引用可重复暴露，换引用当场拒绝；业务运行期只持注入的能力。
    /// </summary>
    public sealed class ClientContext
    {
        private int _assemblySealed;

        /// <summary>装配面是否已密封（全部模块成功，或启动失败/取消的回滚结束）。</summary>
        public bool IsAssemblySealed => Volatile.Read(ref _assemblySealed) != 0;

        internal void SealAssembly() => Interlocked.Exchange(ref _assemblySealed, 1);

        public ServiceContainer Services { get; }

        /// <summary>Host 持有的根作用域（根取消源；模块登记长生命周期资源于此）。</summary>
        public ClientScope RootScope { get; }

        public ClientContext(ClientScope rootScope)
        {
            RootScope = rootScope ?? throw new ArgumentNullException(nameof(rootScope));
            Services = new ServiceContainer(rootScope);
            Services.RegisterInstance(Services);
            Services.RegisterInstance<IServiceContainer>(Services);
            Services.RegisterInstance(rootScope);
        }

        /// <summary>按类型登记模块产物（同类型重复登记即装配错误）。**仅装配期合法**：
        /// 全部模块成功（或启动回滚结束）后装配面密封，运行期调用即抛。</summary>
        public void Put<T>(T product) where T : class
        {
            if (product == null) throw new ArgumentNullException(nameof(product));
            if (IsAssemblySealed)
                throw new InvalidOperationException($"装配已完成——运行期不得新增注册（{typeof(T).Name}）");
            if (!Services.IsSealed && Services.TryGetInstance<T>(out T existing) && ReferenceEquals(existing, product)) return;
            Services.RegisterInstance(product);
        }

        /// <summary>按类型取产物；缺失返回 null（缺失 = 依赖顺序错误或未装配，由消费方决定 fail-fast 语义）。</summary>
        public T Get<T>() where T : class
        {
            return Services.TryResolve<T>(out T product) ? product : null;
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
