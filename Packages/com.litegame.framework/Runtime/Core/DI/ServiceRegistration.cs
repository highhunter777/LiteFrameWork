using System;

namespace LiteFramework
{
    /// <summary>只读服务登记事实；观察者读取它，不参与构造或资源所有权。</summary>
    internal readonly struct ServiceRegistration
    {
        public Type ServiceType { get; }
        public Type ImplementationType { get; }
        public ServiceLifetime Lifetime { get; }
        public object Instance { get; }
        public bool IsConstructed { get; }

        public ServiceRegistration(Type serviceType, Type implementationType, ServiceLifetime lifetime,
            object instance, bool isConstructed)
        {
            ServiceType = serviceType;
            ImplementationType = implementationType;
            Lifetime = lifetime;
            Instance = instance;
            IsConstructed = isConstructed;
        }
    }
}
