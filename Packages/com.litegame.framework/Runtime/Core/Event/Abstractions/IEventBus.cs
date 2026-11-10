using System;

namespace LiteFramework
{
    /// <summary>值型信号委托（in 传递，零拷贝；不参与 StrictMode 的无订阅告警）。</summary>
    public delegate void SignalHandler<T>(in T signal) where T : struct;

    /// <summary>
    /// 事件总线主端口：事实通道（class）+ 值型信号通道 + 延迟/有界队列 + 生命周期。
    /// 作用域内由组合根持有并在 Scope 关闭时 Dispose。
    /// </summary>
    public interface IEventBus : ITickable, IDisposable
    {
        /// <summary>立即派发事实事件；事件所有权归调用方，总线不回收。</summary>
        void Publish<T>(T evt) where T : class;

        /// <summary>订阅事实事件；返回可释放的订阅句柄。</summary>
        IEventSubscription Subscribe<T>(Action<T> handler, int priority = 0) where T : class;

        /// <summary>立即派发值型信号；订阅者异常向调用方上抛并中断本次派发。</summary>
        void PublishSignal<T>(in T signal) where T : struct;

        /// <summary>订阅值型信号；同优先级保持注册序。</summary>
        IEventSubscription SubscribeSignal<T>(SignalHandler<T> handler, int priority = 0) where T : struct;

        bool IsDisposed { get; }

        /// <summary>排队上限；超出丢弃新事件并计数。</summary>
        int MaxQueued { get; }

        /// <summary>延迟派发：到期帧由 Tick 驱动；delayFrames 最小 1。</summary>
        void PublishQueued<T>(T evt, int delayFrames = 1) where T : class;

        /// <summary>默认按构建分流：编辑器/开发构建 true（未订阅事件告警），release false；运行时可覆盖。</summary>
        bool StrictMode { get; set; }
    }

    /// <summary>
    /// Match 作用域事件总线标记：Root 注册 IEventBus，Match 子域注册本接口。
    /// 容器禁止子域覆盖父域同名服务，故用独立标记承载“局内总线”。
    /// </summary>
    public interface IMatchEventBus : IEventBus { }

    /// <summary>订阅句柄：Dispose 注销；幂等。IsDisposed 同时反映总线是否已释放。</summary>
    public interface IEventSubscription : IDisposable
    {
        bool IsDisposed { get; }
    }
}
