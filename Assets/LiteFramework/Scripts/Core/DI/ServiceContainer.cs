using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace LiteFramework
{
    /// <summary>
    /// 主线程显式单例装配器：构造注入、声明图预检、注册冻结、服务所有权与显式帧驱动序。
    /// 构造和工厂只创建对象；异步初始化/关闭归 ClientHost 模块。
    /// AOT 构造反射的实现类型须纳入目标平台保留清单。
    /// </summary>
    public sealed class ServiceContainer : IServiceContainer, IModuleStats, IDisposable
    {
        private sealed class Entry
        {
            public Type Interface;
            public Type Impl;
            public Func<IServiceContainer, object> Factory;
            public Type[] Dependencies;
            public object Instance;
            public ConstructorInfo Ctor;
            public bool Constructed;
            public ServiceLifetime Lifetime;
        }

        public static bool EnableRegistrationTrace;

        private readonly Dictionary<Type, Entry> _entries = new();
        private readonly List<Entry> _order = new();
        private IReadOnlyList<ITickable> _tickables = Array.Empty<ITickable>();
        private IReadOnlyList<IModuleStats> _stats = Array.Empty<IModuleStats>();
        private readonly HashSet<Type> _building = new();
        private readonly List<Type> _chain = new();
        private readonly List<IDisposable> _owned = new();
        private readonly ClientScope _ownershipScope;
        private readonly ServiceContainer _parent;
        private readonly string _scopeName;
        private Type[] _tickOrder;
        private bool _sealed;
        private bool _disposed;

        public bool IsSealed => _sealed;
        public bool IsDisposed => _disposed;
        public IReadOnlyList<ITickable> Tickables => _tickables;
        public IReadOnlyList<IModuleStats> Stats => _stats;
        public IReadOnlyList<Exception> DisposeFailures => _ownershipScope.DisposeFailures;

        /// <summary>创建 Root 容器；子容器只能由 CreateScope 创建，向父域读取 Singleton。</summary>
        public ServiceContainer(ClientScope parent = null)
        {
            _scopeName = "Root";
            _ownershipScope = parent != null ? parent.CreateChild("Services") : new ClientScope("Services");
            parent?.Register(this);
        }

        private ServiceContainer(ServiceContainer parent, string name)
        {
            _parent = parent ?? throw new ArgumentNullException(nameof(parent));
            _scopeName = string.IsNullOrWhiteSpace(name) ? "Scope" : name;
            _ownershipScope = parent._ownershipScope.CreateChild(_scopeName);
            parent._ownershipScope.Register(this);
        }

        public void Register<TInterface, TImpl>(ServiceLifetime lifetime = ServiceLifetime.Singleton) where TImpl : TInterface
        {
            ValidateLifetime(lifetime);
            Add(new Entry { Interface = typeof(TInterface), Impl = typeof(TImpl), Lifetime = lifetime });
        }

        public void RegisterInstance<TInterface>(TInterface instance, ServiceOwnership ownership = ServiceOwnership.Borrowed)
            => RegisterInstance(instance, ServiceLifetime.Singleton, ownership);

        public void RegisterInstance<TInterface>(TInterface instance, ServiceLifetime lifetime, ServiceOwnership ownership = ServiceOwnership.Borrowed)
        {
            ThrowIfRegistrationClosed();
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            ValidateLifetime(lifetime);
            if (ownership != ServiceOwnership.Borrowed && ownership != ServiceOwnership.ContainerOwned)
                throw new ArgumentOutOfRangeException(nameof(ownership));
            var entry = new Entry { Interface = typeof(TInterface), Instance = instance, Constructed = true, Lifetime = lifetime };
            EnsureTickPlanned(entry, instance);
            Add(entry);
            if (ownership == ServiceOwnership.ContainerOwned) Own(instance);
            RebuildDiscovery();
        }

        public void RegisterFactory<TInterface>(Func<IServiceContainer, TInterface> factory, params Type[] dependencies)
            => RegisterFactory(factory, ServiceLifetime.Singleton, dependencies);

        public void RegisterFactory<TInterface>(Func<IServiceContainer, TInterface> factory, ServiceLifetime lifetime, params Type[] dependencies)
        {
            ThrowIfRegistrationClosed();
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            ValidateLifetime(lifetime);
            if (dependencies == null) throw new ArgumentNullException(nameof(dependencies));
            var copy = (Type[])dependencies.Clone();
            for (int i = 0; i < copy.Length; i++)
            {
                if (copy[i] == null) throw new ArgumentException("工厂依赖类型不得为空", nameof(dependencies));
                for (int j = 0; j < i; j++)
                    if (copy[i] == copy[j]) throw new ArgumentException("工厂依赖类型重复", nameof(dependencies));
            }
            Add(new Entry { Interface = typeof(TInterface), Factory = c => factory(c), Dependencies = copy, Lifetime = lifetime });
        }

        public IServiceContainer CreateScope(string name)
        {
            ThrowIfDisposed();
            return new ServiceContainer(this, name);
        }

        private void ValidateLifetime(ServiceLifetime lifetime)
        {
            if (lifetime != ServiceLifetime.Singleton && lifetime != ServiceLifetime.Scoped && lifetime != ServiceLifetime.Transient)
                throw new ArgumentOutOfRangeException(nameof(lifetime));
            if (_parent == null && lifetime == ServiceLifetime.Scoped)
                throw new InvalidOperationException("Root 容器不能注册 Scoped 服务——请先 CreateScope(Account/Match)");
        }

        private void Add(Entry entry)
        {
            ThrowIfRegistrationClosed();
            if (_entries.ContainsKey(entry.Interface))
                throw new InvalidOperationException($"重复注册:{entry.Interface.Name}——同类型只能有一个装配来源");
            if (_parent != null && _parent.CanResolve(entry.Interface))
                throw new InvalidOperationException($"父域已注册:{entry.Interface.Name}——子域不得覆盖 Root 服务");
            _entries.Add(entry.Interface, entry);
            _order.Add(entry);
            if (EnableRegistrationTrace)
                Log.Info($"{entry.Interface.Name} → {(entry.Impl != null ? entry.Impl.Name : "(实例/工厂)")}", "DI");
        }

        public T Resolve<T>() => (T)Resolve(typeof(T));

        public bool TryResolve<T>(out T service)
        {
            ThrowIfDisposed();
            EnsureFactoryDependency(typeof(T));
            if (_entries.ContainsKey(typeof(T)) || (_parent != null && _parent.CanResolve(typeof(T)))) { service = Resolve<T>(); return true; }
            service = default;
            return false;
        }

        public bool TryGetInstance<T>(out T instance)
        {
            ThrowIfDisposed();
            EnsureFactoryDependency(typeof(T));
            if (_entries.TryGetValue(typeof(T), out var entry) && entry.Constructed)
            {
                instance = (T)entry.Instance;
                return true;
            }
            if (_parent != null) return _parent.TryGetInstance(out instance);
            instance = default;
            return false;
        }

        private object Resolve(Type type)
        {
            ThrowIfDisposed();
            EnsureFactoryDependency(type);
            if (!_entries.TryGetValue(type, out var entry))
            {
                if (_parent != null) return _parent.Resolve(type);
                throw new InvalidOperationException($"未注册的服务:{type.Name}——请在客户端装配模块显式注册");
            }
            EnsureLifetimeCapture(type, entry);
            if (entry.Lifetime != ServiceLifetime.Transient && entry.Constructed) return entry.Instance;
            if (!_building.Add(type))
                throw new InvalidOperationException($"循环依赖: {FormatChain(_chain, type)}");
            _chain.Add(type);
            try
            {
                object instance = entry.Factory != null ? entry.Factory(this) : Construct(entry);
                if (instance == null) throw new InvalidOperationException($"工厂返回空服务:{type.Name}");
                // 先登记所有权：驱动契约拒绝新对象时，失败回滚仍能释放它。
                Own(instance);
                ThrowIfDisposed();
                EnsureTickPlanned(entry, instance);
                if (entry.Lifetime != ServiceLifetime.Transient)
                {
                    entry.Instance = instance;
                    entry.Constructed = true;
                }
                RebuildDiscovery();
                return instance;
            }
            finally
            {
                _chain.RemoveAt(_chain.Count - 1);
                _building.Remove(type);
            }
        }

        private void EnsureFactoryDependency(Type type)
        {
            if (_chain.Count > 0)
            {
                var caller = _entries[_chain[_chain.Count - 1]];
                if (caller.Factory != null && Array.IndexOf(caller.Dependencies, type) < 0)
                    throw new InvalidOperationException($"工厂依赖未声明:{caller.Interface.Name} → {type.Name}");
            }
        }

        private void EnsureLifetimeCapture(Type type, Entry dependency)
        {
            if (_chain.Count == 0) return;
            Entry caller = _entries[_chain[_chain.Count - 1]];
            if (caller.Lifetime == ServiceLifetime.Singleton && dependency.Lifetime == ServiceLifetime.Scoped)
                throw new InvalidOperationException($"生命周期越域捕获:{caller.Interface.Name}(Singleton) → {type.Name}(Scoped)——短生命周期不能进入长生命周期");
        }

        private bool CanResolve(Type type)
        {
            return _entries.ContainsKey(type) || (_parent != null && _parent.CanResolve(type));
        }

        private object Construct(Entry entry)
        {
            PrepareConstructor(entry);
            var parameters = entry.Ctor.GetParameters();
            var args = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++) args[i] = Resolve(parameters[i].ParameterType);
            try { return entry.Ctor.Invoke(args); }
            catch (TargetInvocationException error)
            {
                var inner = error.InnerException ?? error;
                throw new InvalidOperationException($"构造抛异常:{entry.Impl.Name}({inner.Message})", inner);
            }
        }

        /// <summary>只遍历声明图；构造和工厂均不执行。</summary>
        public void Validate()
        {
            ThrowIfDisposed();
            var complete = new HashSet<Type>();
            var active = new HashSet<Type>();
            var path = new List<Type>();
            foreach (var entry in _order) ValidateEntry(entry.Interface, complete, active, path);
            if (_tickOrder != null) ValidateTickOrder(_tickOrder);
        }

        private void ValidateEntry(Type type, HashSet<Type> complete, HashSet<Type> active, List<Type> path)
        {
            if (!_entries.TryGetValue(type, out var entry))
            {
                if (_parent != null)
                {
                    _parent.ValidateEntry(type, complete, active, path);
                    return;
                }
                throw new InvalidOperationException($"依赖未注册: {FormatChain(path, type)}");
            }
            if (complete.Contains(type)) return;
            if (!active.Add(type)) throw new InvalidOperationException($"循环依赖: {FormatChain(path, type)}");
            path.Add(type);
            if (entry.Impl != null) PrepareConstructor(entry);
            if (entry.Lifetime == ServiceLifetime.Transient &&
                (entry.Impl != null ? typeof(ITickable).IsAssignableFrom(entry.Impl) : typeof(ITickable).IsAssignableFrom(entry.Interface)))
                throw new InvalidOperationException($"Transient 服务不能进入帧驱动:{entry.Interface.Name}");
            if (entry.Dependencies != null)
                foreach (Type dependency in entry.Dependencies)
                {
                    if (entry.Lifetime == ServiceLifetime.Singleton &&
                        TryFindEntry(dependency, out var dependencyEntry) && dependencyEntry.Lifetime == ServiceLifetime.Scoped)
                        throw new InvalidOperationException($"生命周期越域捕获:{entry.Interface.Name}(Singleton) → {dependency.Name}(Scoped)——短生命周期不能进入长生命周期");
                    ValidateEntry(dependency, complete, active, path);
                }
            path.RemoveAt(path.Count - 1);
            active.Remove(type);
            complete.Add(type);
        }

        private bool TryFindEntry(Type type, out Entry entry)
        {
            if (_entries.TryGetValue(type, out entry)) return true;
            if (_parent != null) return _parent.TryFindEntry(type, out entry);
            entry = null;
            return false;
        }

        private static void PrepareConstructor(Entry entry)
        {
            if (entry.Ctor != null) return;
            Type impl = entry.Impl;
            if (impl.IsAbstract || impl.IsInterface)
                throw new InvalidOperationException($"实现类型不能构造:{impl.Name}");
            ConstructorInfo best = null;
            int max = -1;
            bool ambiguous = false;
            foreach (var ctor in impl.GetConstructors(BindingFlags.Instance | BindingFlags.Public))
            {
                int count = ctor.GetParameters().Length;
                if (count > max) { max = count; best = ctor; ambiguous = false; }
                else if (count == max) ambiguous = true;
            }
            if (best == null) throw new InvalidOperationException($"无公共构造函数:{impl.Name}");
            if (ambiguous) throw new InvalidOperationException($"构造函数歧义:{impl.Name} 有多个 {max} 参公共构造");
            entry.Ctor = best;
            var parameters = best.GetParameters();
            entry.Dependencies = new Type[parameters.Length];
            for (int i = 0; i < parameters.Length; i++) entry.Dependencies[i] = parameters[i].ParameterType;
        }

        public void SetTickOrder(params Type[] serviceTypes)
        {
            ThrowIfRegistrationClosed();
            if (serviceTypes == null) throw new ArgumentNullException(nameof(serviceTypes));
            var copy = (Type[])serviceTypes.Clone();
            ValidateTickOrder(copy);
            _tickOrder = copy;
            RebuildDiscovery();
        }

        private void ValidateTickOrder(Type[] plan)
        {
            var types = new HashSet<Type>();
            foreach (Type type in plan)
            {
                if (type == null) throw new ArgumentException("驱动类型不得为空");
                if (!types.Add(type)) throw new InvalidOperationException($"驱动类型重复:{type.Name}");
                if (!_entries.TryGetValue(type, out var entry))
                    throw new InvalidOperationException($"驱动服务未注册:{type.Name}");
                if (entry.Lifetime == ServiceLifetime.Transient)
                    throw new InvalidOperationException($"Transient 服务不能进入帧驱动:{type.Name}");
                if (entry.Constructed ? !(entry.Instance is ITickable) :
                    entry.Impl != null ? !typeof(ITickable).IsAssignableFrom(entry.Impl) :
                    !typeof(ITickable).IsAssignableFrom(type))
                    throw new InvalidOperationException($"驱动服务未实现 ITickable:{type.Name}");
            }
            foreach (var entry in _order)
            {
                bool tickable = entry.Constructed ? entry.Instance is ITickable :
                    entry.Impl != null ? typeof(ITickable).IsAssignableFrom(entry.Impl) :
                    typeof(ITickable).IsAssignableFrom(entry.Interface);
                if (tickable && !IsTickPlanned(entry, entry.Instance, plan))
                    throw new InvalidOperationException($"驱动服务未编排:{entry.Interface.Name}");
            }
        }

        private bool IsTickPlanned(Entry entry, object instance, Type[] plan)
        {
            foreach (Type type in plan)
            {
                if (type == entry.Interface) return true;
                if (instance != null && _entries.TryGetValue(type, out var alias) &&
                    alias.Constructed && ReferenceEquals(alias.Instance, instance)) return true;
            }
            return false;
        }

        private void EnsureTickPlanned(Entry entry, object instance)
        {
            if (entry.Lifetime == ServiceLifetime.Transient && instance is ITickable)
                throw new InvalidOperationException($"Transient 服务不能进入帧驱动:{entry.Interface.Name}——Tickable 必须是 Singleton 或 Scoped");
            if (_tickOrder != null && instance is ITickable && !IsTickPlanned(entry, instance, _tickOrder))
                throw new InvalidOperationException($"驱动服务未编排:{entry.Interface.Name}");
        }

        private void RebuildDiscovery()
        {
            var tickables = new List<ITickable>();
            var stats = new List<IModuleStats>();
            foreach (var entry in _order)
            {
                if (_tickOrder == null && entry.Instance is ITickable tickable) AddReference(tickables, tickable);
                if (entry.Instance is IModuleStats item) AddReference(stats, item);
            }
            if (_tickOrder != null)
                foreach (Type type in _tickOrder)
                    if (_entries[type].Instance is ITickable tickable) AddReference(tickables, tickable);
            _tickables = tickables.AsReadOnly();
            _stats = stats.AsReadOnly();
        }

        private void Own(object instance)
        {
            if (!(instance is IDisposable disposable)) return;
            foreach (IDisposable current in _owned) if (ReferenceEquals(current, disposable)) return;
            _ownershipScope.Register(disposable);
            _owned.Add(disposable);
        }

        private static bool AddReference<T>(List<T> list, T instance) where T : class
        {
            foreach (T current in list) if (ReferenceEquals(current, instance)) return false;
            list.Add(instance);
            return true;
        }

        public void Seal()
        {
            ThrowIfRegistrationClosed();
            Validate();
            _sealed = true;
        }

        private void ThrowIfRegistrationClosed()
        {
            ThrowIfDisposed();
            if (_sealed) throw new InvalidOperationException("容器已密封——装配完成后一切注册均拒绝");
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ServiceContainer));
        }

        private static string FormatChain(List<Type> chain, Type requested)
        {
            var text = new StringBuilder();
            foreach (Type type in chain) text.Append(type.Name).Append(" → ");
            return text.Append(requested.Name).ToString();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _ownershipScope.Dispose();
            _owned.Clear();
            _tickables = Array.Empty<ITickable>();
            _stats = Array.Empty<IModuleStats>();
            _entries.Clear();
            _order.Clear();
            _tickOrder = null;
        }

        public string StatsName => "DI";
        public void Snapshot(Dictionary<string, string> into)
        {
            int built = 0;
            foreach (var entry in _order) if (entry.Constructed) built++;
            into["注册数"] = _entries.Count.ToString();
            into["已构造"] = built.ToString();
            into["Tickable"] = _tickables.Count.ToString();
            into["已密封"] = _sealed ? "是" : "否";
            into["已释放"] = _disposed ? "是" : "否";
            into["释放异常"] = DisposeFailures.Count.ToString();
        }
    }
}
