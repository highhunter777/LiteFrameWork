using System;
using System.Collections.Generic;

namespace LiteFramework
{
    /// <summary>主线程帧计划：覆盖校验、别名去重、稳定驱动顺序与逐帧执行。</summary>
    public sealed class FrameSchedule : IModuleStats, IDisposable
    {
        private ServiceContainer _services;
        private Type[] _order;
        private IReadOnlyList<ITickable> _tickables = Array.Empty<ITickable>();

        public bool IsDisposed { get; private set; }
        public IReadOnlyList<ITickable> Tickables => _tickables;
        public string StatsName => "Frames";

        public FrameSchedule(ServiceContainer services)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            Validate();
            Rebuild();
            services.ValidatingRegistration += ValidateRegistration;
            services.RegistrationsChanged += OnRegistrationsChanged;
            services.Validating += Validate;
        }

        /// <summary>设置显式帧序；无构造副作用，校验失败保留原计划，容器密封后不可修改。</summary>
        public void SetOrder(params Type[] serviceTypes)
        {
            ThrowIfDisposed();
            if (_services.IsSealed) throw new InvalidOperationException("容器已密封——帧计划不可修改");
            if (serviceTypes == null) throw new ArgumentNullException(nameof(serviceTypes));
            var copy = (Type[])serviceTypes.Clone();
            ValidateOrder(copy, _services.SnapshotRegistrations());
            _order = copy;
            Rebuild();
        }

        /// <summary>校验登记事实，不解析服务或执行工厂。</summary>
        public void Validate()
        {
            ThrowIfDisposed();
            ServiceRegistration[] registrations = _services.SnapshotRegistrations();
            foreach (var registration in registrations) ValidateLifetime(registration);
            if (_order != null) ValidateOrder(_order, registrations);
        }

        /// <summary>使用当前帧的稳定快照，按声明顺序传递同一真实帧间隔。</summary>
        public void Tick(float realDelta)
        {
            ThrowIfDisposed();
            IReadOnlyList<ITickable> snapshot = _tickables;
            for (int i = 0; i < snapshot.Count; i++) snapshot[i].Tick(realDelta);
        }

        public void Snapshot(Dictionary<string, string> into)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));
            into.Clear();
            into["驱动数"] = _tickables.Count.ToString();
            into["显式计划"] = _order != null ? "是" : "否";
            into["已释放"] = IsDisposed ? "是" : "否";
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            _services.ValidatingRegistration -= ValidateRegistration;
            _services.RegistrationsChanged -= OnRegistrationsChanged;
            _services.Validating -= Validate;
            _services = null;
            _order = null;
            _tickables = Array.Empty<ITickable>();
        }

        private static bool IsTickable(ServiceRegistration registration)
            => registration.IsConstructed ? registration.Instance is ITickable
                : typeof(ITickable).IsAssignableFrom(registration.ImplementationType ?? registration.ServiceType);

        private static void ValidateLifetime(ServiceRegistration registration)
        {
            if (registration.Lifetime == ServiceLifetime.Transient && IsTickable(registration))
                throw new InvalidOperationException($"Transient 服务不能进入帧驱动:{registration.ServiceType.Name}");
        }

        private void ValidateRegistration(ServiceRegistration registration)
        {
            ValidateLifetime(registration);
            if (_order != null && IsTickable(registration)
                && !IsPlanned(registration, _order, _services.SnapshotRegistrations()))
                throw new InvalidOperationException($"驱动服务未编排:{registration.ServiceType.Name}");
        }

        private static void ValidateOrder(Type[] order, ServiceRegistration[] registrations)
        {
            var types = new HashSet<Type>();
            foreach (Type type in order)
            {
                if (type == null) throw new ArgumentException("驱动类型不得为空");
                if (!types.Add(type)) throw new InvalidOperationException($"驱动类型重复:{type.Name}");
                if (!TryFind(registrations, type, out var registration))
                    throw new InvalidOperationException($"驱动服务未注册:{type.Name}");
                if (registration.Lifetime == ServiceLifetime.Transient)
                    throw new InvalidOperationException($"Transient 服务不能进入帧驱动:{type.Name}");
                if (!IsTickable(registration))
                    throw new InvalidOperationException($"驱动服务未实现 ITickable:{type.Name}");
            }
            foreach (var registration in registrations)
                if (IsTickable(registration) && !IsPlanned(registration, order, registrations))
                    throw new InvalidOperationException($"驱动服务未编排:{registration.ServiceType.Name}");
        }

        private static bool IsPlanned(ServiceRegistration registration, Type[] order, ServiceRegistration[] registrations)
        {
            foreach (Type type in order)
            {
                if (type == registration.ServiceType) return true;
                if (registration.Instance != null && TryFind(registrations, type, out var alias)
                    && alias.IsConstructed && ReferenceEquals(alias.Instance, registration.Instance)) return true;
            }
            return false;
        }

        private static bool TryFind(ServiceRegistration[] registrations, Type type, out ServiceRegistration registration)
        {
            foreach (var item in registrations)
                if (item.ServiceType == type) { registration = item; return true; }
            registration = default;
            return false;
        }

        private void OnRegistrationsChanged()
        {
            if (_services.IsDisposed) Dispose();
            else Rebuild();
        }

        private void Rebuild()
        {
            ServiceRegistration[] registrations = _services.SnapshotRegistrations();
            var tickables = new List<ITickable>();
            if (_order == null)
            {
                foreach (var registration in registrations) AddTickable(tickables, registration.Instance);
            }
            else
            {
                foreach (Type type in _order)
                    if (TryFind(registrations, type, out var registration)) AddTickable(tickables, registration.Instance);
            }
            _tickables = tickables.AsReadOnly();
        }

        private static void AddTickable(List<ITickable> tickables, object instance)
        {
            if (!(instance is ITickable tickable)) return;
            foreach (var item in tickables) if (ReferenceEquals(item, tickable)) return;
            tickables.Add(tickable);
        }

        private void ThrowIfDisposed()
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(FrameSchedule));
        }
    }
}
