using System;

namespace LiteFramework
{
    /// <summary>
    /// 发布拦截器（《事件中心专项设计》§3.5）：Publish 管线在派发之前按注册序运行全部已注册拦截器。
    /// 返回 true = 放行（观测/计数）；返回 false = 否决——该次发布被丢弃（不派发、不计 StrictMode 空转、
    /// 否决路径池化事件同样回收）。拦截器异常 = 记日志跳过继续管线（fail-open）。
    /// </summary>
    public interface IEventInterceptor
    {
        /// <summary>拦截判定。eventType = 事件类型；e = 事件实例（只读观测——否决后所有权归中心回收）。</summary>
        bool Intercept(Type eventType, object e);
    }
}
