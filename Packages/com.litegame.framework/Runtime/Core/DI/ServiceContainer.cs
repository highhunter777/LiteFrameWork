using System;
using System.Collections.Generic;

namespace LiteFramework
{
    /// <summary>主线程服务注册表：实例登记、按类型解析、注册冻结与服务所有权。</summary>
    public sealed class ServiceContainer : IModuleStats, IDisposable
    {
        private sealed class Entry
        {
            public Type ServiceType;
            public object Instance;
        }

        private readonly Dictionary<Type, Entry> _entries = new();

        private readonly List<Entry> _order = new();

        private readonly List<IDisposable> _owned = new();

        private readonly ClientScope _ownershipScope;

        private IReadOnlyList<IModuleStats> _stats = Array.Empty<IModuleStats>();

        private bool _sealed;

        private bool _disposed;

        public bool IsSealed => _sealed;

        public bool IsDisposed => _disposed;

        public IReadOnlyList<IModuleStats> Stats => _stats;

        public IReadOnlyList<Exception> DisposeFailures => _ownershipScope.DisposeFailures;

        internal event Action RegistrationsChanged;

        /// <summary>创建注册表；传入父作用域时，表随该作用域释放（服务资源另有独立子作用域）。</summary>
        public ServiceContainer(ClientScope parent = null)
        {
            _ownershipScope = parent != null ? parent.CreateChild("Services") : new ClientScope("Services");
            parent?.Register(this);
        }

        /// <summary>登记一个实例；同类型只能有一个来源，重复即抛。</summary>
        public void RegisterInstance<TInterface>(TInterface instance, ServiceOwnership ownership = ServiceOwnership.Borrowed)
        {
            ThrowIfRegistrationClosed();
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            if (ownership != ServiceOwnership.Borrowed && ownership != ServiceOwnership.ContainerOwned)
                throw new ArgumentOutOfRangeException(nameof(ownership));

            Type serviceType = typeof(TInterface);
            if (_entries.ContainsKey(serviceType))
                throw new InvalidOperationException($"重复注册:{serviceType.Name}——同类型只能有一个装配来源");

            var entry = new Entry { ServiceType = serviceType, Instance = instance };
            _entries.Add(serviceType, entry);
            _order.Add(entry);
            if (ownership == ServiceOwnership.ContainerOwned) Own(instance);
            RebuildStats();
            RegistrationsChanged?.Invoke();
        }

        public T Resolve<T>() => (T)Resolve(typeof(T));

        /// <summary>只查询已登记实例；缺失返回 false，不构造、不抛（未注册的解析才抛）。</summary>
        public bool TryResolve<T>(out T service)
        {
            ThrowIfDisposed();
            if (_entries.TryGetValue(typeof(T), out var entry))
            {
                service = (T)entry.Instance;
                return true;
            }
            service = default;
            return false;
        }

        /// <summary>冻结注册；失败/未冻结前可继续登记。密封后帧计划只做一次覆盖校验。</summary>
        public void Seal()
        {
            ThrowIfRegistrationClosed();
            _sealed = true;
            RegistrationsChanged?.Invoke();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _ownershipScope.Dispose();
            _owned.Clear();
            _stats = Array.Empty<IModuleStats>();
            _entries.Clear();
            _order.Clear();
            Action changed = RegistrationsChanged;
            RegistrationsChanged = null;
            changed?.Invoke();
        }

        public string StatsName => "DI";

        public void Snapshot(Dictionary<string, string> into)
        {
            into.Clear();                                    // 契约：实现 Clear + 填（HUD 不清，全实现统一）
            into["注册数"] = _entries.Count.ToString();
            into["已密封"] = _sealed ? "是" : "否";
            into["已释放"] = _disposed ? "是" : "否";
            into["释放异常"] = DisposeFailures.Count.ToString();
        }

        internal (Type ServiceType, object Instance)[] SnapshotRegistrations()
        {
            ThrowIfDisposed();
            var registrations = new (Type ServiceType, object Instance)[_order.Count];
            for (int i = 0; i < _order.Count; i++)
                registrations[i] = (_order[i].ServiceType, _order[i].Instance);
            return registrations;
        }

        private object Resolve(Type type)
        {
            ThrowIfDisposed();
            if (_entries.TryGetValue(type, out var entry)) return entry.Instance;
            throw new InvalidOperationException($"未注册的服务:{type.Name}——请在客户端装配模块显式注册");
        }

        private void Own(object instance)
        {
            if (!(instance is IDisposable disposable)) return;
            foreach (IDisposable current in _owned) if (ReferenceEquals(current, disposable)) return;
            _ownershipScope.Register(disposable);
            _owned.Add(disposable);
        }

        private void RebuildStats()
        {
            var stats = new List<IModuleStats>();
            foreach (var entry in _order)
                if (entry.Instance is IModuleStats item && !ContainsReference(stats, item)) stats.Add(item);
            _stats = stats.AsReadOnly();
        }

        private static bool ContainsReference(List<IModuleStats> list, IModuleStats instance)
        {
            foreach (IModuleStats current in list) if (ReferenceEquals(current, instance)) return true;
            return false;
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
    }
}
