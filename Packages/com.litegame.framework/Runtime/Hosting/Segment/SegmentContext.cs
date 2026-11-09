using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteFramework
{

    /// <summary>模块作用域实现：包一层根作用域的子 <see cref="ClientScope"/>（LIFO 释放 + 取消链接）。
    /// 每个模块一个实例；Host 持有并在关闭/回滚路径释放。</summary>
    internal sealed class SegmentScope : ISegmentScope, IDisposable
    {
        private readonly ClientScope _scope;

        internal SegmentScope(ClientScope root, string moduleName)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            _scope = root.CreateChild("Module:" + moduleName);
        }

        public string Name => _scope.Name;
        public CancellationToken Token => _scope.Token;
        public int OwnedCount => _scope.OwnedCount;
        public IReadOnlyList<Exception> DisposeFailures => _scope.DisposeFailures;
        internal bool IsDisposed => _scope.IsDisposed;

        public void Register(IDisposable resource) => _scope.Register(resource);

        public void Dispose() => _scope.Dispose();
    }

    /// <summary>
    /// 模块装配窗口（路线图 §4.3）：<see cref="IClientSegment.InitializeAsync"/> 唯一的登记面。
    /// </summary>
    public sealed class SegmentContext
    {
        private readonly ClientContext _context;
        private readonly string _moduleName;
        private int _open = 1;

        internal SegmentContext(ClientContext context, string moduleName, ISegmentScope scope)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _moduleName = moduleName ?? throw new ArgumentNullException(nameof(moduleName));
            Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        }

        /// <summary>窗口所属模块名（诊断/异常消息用）。</summary>
        public string ModuleName => _moduleName;

        /// <summary>本模块的资源作用域（初始化期资源登记在这里，随模块关闭释放）。</summary>
        public ISegmentScope Scope { get; }

        /// <summary>根容器（模块装配期登记服务用；与 <see cref="Put{T}"/> 同一来源）。</summary>
        public ServiceContainer Services => _context.Services;

        /// <summary>根作用域（模块登记长生命周期资源/取根令牌）。</summary>
        public ClientScope RootScope => _context.RootScope;

        /// <summary>装配期登记产物。窗口关闭后（本模块初始化已返回）一律抛出——登记不得跨模块/跨帧。</summary>
        public void Put<T>(T product) where T : class
        {
            EnsureOpen("Put");
            _context.Put(product, _moduleName);
        }

        /// <summary>装配期登记实例（直接走容器的形态；与 <see cref="Put{T}"/> 同窗口约束，提供段同样入报告）。</summary>
        public void RegisterInstance<T>(T instance, ServiceOwnership ownership = ServiceOwnership.Borrowed) where T : class
        {
            EnsureOpen("RegisterInstance");
            Services.RegisterInstance(instance, ownership);
            _context.RecordProvider(typeof(T), _moduleName);
        }

        /// <summary>按类型取产物；缺失返回 null（由消费方决定 fail-fast 语义）。</summary>
        public T Get<T>() where T : class
        {
            T product = _context.Get<T>();
            _context.RecordConsumer(typeof(T), _moduleName);
            return product;
        }

        /// <summary>按类型取产物；缺失即抛（消息带模块名与排查方向）。</summary>
        public T Require<T>() where T : class
        {
            T product = _context.Get<T>();
            _context.RecordConsumer(typeof(T), _moduleName);
            if (product == null)
                throw new InvalidOperationException(
                    $"模块 {_moduleName} Require 了未装配的产物 {typeof(T).Name}——检查提供段是否 Put、以及 DependsOn 是否缺声明");
            return product;
        }

        private void EnsureOpen(string surface)
        {
            if (Volatile.Read(ref _open) == 0)
                throw new InvalidOperationException(
                    $"模块 {_moduleName} 的装配窗口已关闭——{surface} 只允许在本模块 InitializeAsync 内（路线图 §4.3）");
        }

        internal void Close() => Interlocked.Exchange(ref _open, 0);
    }

    /// <summary>
    /// 启动装配上下文：模块产物与 Root 服务容器共享同一来源。
    /// 同类型同引用可重复暴露，换引用当场拒绝；业务运行期只持注入的能力。
    /// </summary>
    public sealed class ClientContext
    {
        private int _assemblySealed;
        private readonly AssemblyLedger _ledger = new AssemblyLedger();

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
            Services.RegisterInstance(new FrameSchedule(Services), ServiceOwnership.ContainerOwned);
            Services.RegisterInstance(Services);
            Services.RegisterInstance<IServiceContainer>(Services);
            Services.RegisterInstance(rootScope);
            _ledger.RecordProvider(typeof(FrameSchedule), AssemblyLedger.FrameworkProvider);
            _ledger.RecordProvider(typeof(ServiceContainer), AssemblyLedger.FrameworkProvider);
            _ledger.RecordProvider(typeof(IServiceContainer), AssemblyLedger.FrameworkProvider);
            _ledger.RecordProvider(typeof(ClientScope), AssemblyLedger.FrameworkProvider);
        }

        /// <summary>按类型登记模块产物（同类型重复登记即装配错误）。**仅装配期合法**：
        /// 全部模块成功（或启动回滚结束）后装配面密封，运行期调用即抛。</summary>
        public void Put<T>(T product) where T : class => Put(product, AssemblyLedger.FrameworkProvider);

        /// <summary>登记产物并记录提供段（装配报告来源；同型同引用重复暴露保留首个提供段）。</summary>
        internal void Put<T>(T product, string provider) where T : class
        {
            if (product == null) throw new ArgumentNullException(nameof(product));
            if (IsAssemblySealed)
                throw new InvalidOperationException($"装配已完成——运行期不得新增注册（{typeof(T).Name}）");
            if (!Services.IsSealed && Services.TryGetInstance<T>(out T existing) && ReferenceEquals(existing, product))
            {
                RecordProvider(typeof(T), provider);
                return;
            }
            Services.RegisterInstance(product);
            RecordProvider(typeof(T), provider);
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

        internal void RecordProvider(Type type, string provider) => _ledger.RecordProvider(type, provider);

        internal void RecordConsumer(Type type, string consumer)
        {
            if (IsAssemblySealed) return;                       // 运行期读取不入报告（报告密封后即定稿）
            _ledger.RecordConsumer(type, consumer);
        }

        /// <summary>构建装配报告（密封后调用一次；只读快照，不再随运行期变化）。</summary>
        internal AssemblyReport BuildReport(IReadOnlyList<string> order) => _ledger.Build(order);
    }
}
