using System;
using System.Collections;
using System.Collections.Generic;


namespace LiteFramework
{
    public interface IServiceContainer
    {
        void Register<TInterface, TImpl>() where TImpl : TInterface;   // 仅单例；首次 Resolve 时构造
        void RegisterInstance<TInterface>(TInterface instance);
        void RegisterFactory<TInterface>(Func<IServiceContainer, TInterface> factory);  // 延迟构造，结果同样按单例缓存
        T Resolve<T>();
        void Seal();                          // 装配密封：此后一切 Register* 抛；由装配根模块在装配末尾调用
        IReadOnlyList<ITickable> Tickables { get; }   // 注册即发现：注册顺序 = 驱动顺序
        IReadOnlyList<IModuleStats> Stats { get; }  // 同上，HUD 数据源
    }
}
