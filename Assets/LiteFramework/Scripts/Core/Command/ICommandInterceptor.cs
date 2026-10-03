using System;

namespace LiteFramework
{
    /// <summary>命令拦截器（《命令中心专项设计》§4）：Send 执行序 = 权限门 → 拦截器链 → 处理器。
    /// 链按登记序运行，任一拒绝即终止（CommandReject.InterceptorRejected）。定位：频率限制、审计、GM 防滥用。</summary>
    public interface ICommandInterceptor
    {
        /// <summary>拦截判定：true 放行 → 链继续/进处理器；false = 拒绝（不执行处理器）。</summary>
        bool Intercept(Type commandType, object command);
    }
}
