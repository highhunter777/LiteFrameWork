using System;
using System.Collections.Generic;


namespace LiteFramework
{
    public interface IServiceContainer
    {
        void Register<TInterface, TImpl>(ServiceLifetime lifetime = ServiceLifetime.Singleton) where TImpl : TInterface;
        void RegisterInstance<TInterface>(TInterface instance, ServiceOwnership ownership = ServiceOwnership.Borrowed);
        void RegisterInstance<TInterface>(TInterface instance, ServiceLifetime lifetime, ServiceOwnership ownership = ServiceOwnership.Borrowed);
        void RegisterFactory<TInterface>(Func<IServiceContainer, TInterface> factory, params Type[] dependencies);
        void RegisterFactory<TInterface>(Func<IServiceContainer, TInterface> factory, ServiceLifetime lifetime, params Type[] dependencies);
        IServiceContainer CreateScope(string name);
        T Resolve<T>();
        bool TryResolve<T>(out T service);
        bool TryGetInstance<T>(out T instance); // 只查询已有实例，不构造；装配面同引用检查
        void Validate();                       // 只校验声明图，无构造/工厂副作用
        void SetTickOrder(params Type[] serviceTypes);
        void Seal();                          // 预检后密封；此后一切 Register* 抛
        IReadOnlyList<ITickable> Tickables { get; }   // 显式驱动序；未设置时按注册序发现
        IReadOnlyList<IModuleStats> Stats { get; }  // 同上，HUD 数据源
    }
}
