using System;

namespace LiteNet.Transport
{
    /// <summary>
    /// 传输错误诊断端口（可选能力，M3-6b 解耦用）：实现方在协议/载荷层发现错误时上报一行文本。
    /// </summary>
    public interface IClientTransportErrorSource
    {
        /// <summary>错误文本（诊断面消费：会话语义仍由相位机承担）。</summary>
        event Action<string> OnError;
    }
}
