using System;

namespace LiteFramework
{
    /// <summary>
    /// 统一错误分类（《客户端依赖注入专项设计》§6.2）：分类在**抛出点**声明，不在 catch 点猜测。
    /// 错误页/诊断只消费本枚举与恢复结果，不另造错误码。
    /// </summary>
    public enum ErrorKind
    {
        /// <summary>瞬时故障（网络/CDN 抖动）→ 有界重试。</summary>
        Transient = 0,

        /// <summary>可恢复（缓存损坏/旧清单等确定性但可修复）→ 修复后重试（清缓存/回登录）。</summary>
        Recoverable = 1,

        /// <summary>兼容性（客户端/内容/协议不匹配）→ 阻断并提示更新。</summary>
        Compatibility = 2,

        /// <summary>安全（签名失败/票据篡改/重放）→ 不重试，导出诊断并阻断。</summary>
        Security = 3,

        /// <summary>致命（内置资源缺失/初始化不变量破坏）→ 导出诊断并安全退出。</summary>
        Fatal = 4,
    }

    /// <summary>
    /// 错误分类工具：只认 <see cref="ClientErrorException"/> 的显式标记；未标记 = 无法分类 = <see cref="ErrorKind.Fatal"/>。
    /// 取消（<see cref="OperationCanceledException"/>）不是错误，调用方先用 <see cref="IsCancellation"/> 判定。
    /// </summary>
    public static class ErrorClassification
    {
        /// <summary>取消 ≠ 错误（不触发恢复动作）。</summary>
        public static bool IsCancellation(Exception error) => error is OperationCanceledException;

        /// <summary>错误 → 分类（<see cref="ClientErrorException"/> 透传标记；其余 Fatal）。</summary>
        public static ErrorKind Of(Exception error)
        {
            if (error is ClientErrorException marked) return marked.Kind;
            return ErrorKind.Fatal;
        }
    }

    /// <summary>
    /// 带分类的客户端错误：抛出方标记 <see cref="Kind"/>（分类在抛出点声明），恢复执行器按分类选动作。
    /// </summary>
    public class ClientErrorException : Exception
    {
        public ErrorKind Kind { get; }

        public ClientErrorException(ErrorKind kind, string message, Exception inner = null)
            : base(message, inner)
        {
            Kind = kind;
        }
    }
}
