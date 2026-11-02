using System;

namespace LiteNet.Transport
{
    /// <summary>
    /// 传输错误诊断端口（可选能力，M3-6b 解耦用）：实现方在协议/载荷层发现错误时上报一行文本。
    /// 目的是让"开发期装饰器（弱网注入）"与"产品会话"解耦——产品侧只按本接口**可选订阅**，
    /// 不认识具体装饰器类型（弱网件只在开发宏内编译，release 不出现）。
    /// </summary>
    public interface IClientTransportErrorSource
    {
        /// <summary>错误文本（诊断面消费：会话语义仍由相位机承担）。</summary>
        event Action<string> OnError;
    }
}
