using System;

namespace LiteFramework
{
    /// <summary>发布端口（ISP）：只暴露派发，不暴露订阅/队列。</summary>
    public interface IEventPublisher
    {
        /// <summary>立即派发。不接管事件所有权；池化事件用 <see cref="IEventBus.PublishPooled{T}"/>。</summary>
        void Publish<T>(T evt) where T : class;
    }

    /// <summary>订阅端口（ISP）：返回可释放的订阅句柄。</summary>
    public interface IEventSubscriber
    {
        IEventSubscription Subscribe<T>(Action<T> handler, int priority = 0) where T : class;
    }

    /// <summary>
    /// 事件总线主端口：发布 + 订阅 + 池化发布 + 延迟队列 + 生命周期。
    /// 静态/单例总线不得绕过本接口；作用域内由组合根持有并在 Scope 关闭时 Dispose。
    /// </summary>
    public interface IEventBus : IEventPublisher, IEventSubscriber, IDisposable
    {
        bool IsDisposed { get; }

        /// <summary>显式池化发布：Publish 后由中心回收（仅 IReference 事件）。普通事件请用 Publish。</summary>
        void PublishPooled<T>(T evt) where T : class, IReference;

        /// <summary>入队，Tick 统一派发（低频便利）。队列接管池化事件到派发/丢弃完成。</summary>
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
