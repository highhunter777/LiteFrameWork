using System;
using System.Collections.Generic;

namespace LiteFramework
{
    /// <summary>
    /// 事件接收位（层级状态机的冒泡）：**可选实现**——不实现就是"不处理事件"。
    /// 一个阶段可实现多个 `IEventSink&lt;T&gt;` 以处理多种事件类型。
    /// </summary>
    public interface IEventSink<TEvt>
    {
        /// <summary>处理事件：返回 true = 已消费（冒泡停止）。</summary>
        bool TryHandle(in TEvt e);
    }
}
