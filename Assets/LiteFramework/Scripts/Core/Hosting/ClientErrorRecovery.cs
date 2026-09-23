using System;

namespace LiteFramework
{
    /// <summary>
    /// 启动/运行错误的恢复分类（《商业级通用客户端框架总设计》§7.2 错误分类表——
    /// 错误类型决定默认动作：Transient 重试 / Recoverable 清缓存 / Compatibility 阻止 / Security 不重试 / Fatal 退出）。
    /// C1 只冻结分类与入口骨架；具体恢复动作的实装（下载重试/重新登录/内容回滚）随 C2/C3 主流程落地。
    /// </summary>
    public enum ClientErrorKind
    {
        /// <summary>瞬时故障（DNS/超时/短暂 CDN 失败）→ 有界退避重试，允许用户取消。</summary>
        Transient = 0,

        /// <summary>可恢复（缓存损坏/旧清单/Token 过期）→ 清受控缓存、回滚或重新登录。</summary>
        Recoverable = 1,

        /// <summary>兼容性（app/content/protocol 不兼容）→ 阻止进入，提示更新客户端。</summary>
        Compatibility = 2,

        /// <summary>安全（签名失败/票据篡改/重放）→ 不重试，清敏感状态并上报。</summary>
        Security = 3,

        /// <summary>致命（内置资源缺失/初始化不变量破坏）→ 导出诊断，安全退出/重启。</summary>
        Fatal = 4,
    }

    /// <summary>
    /// 错误恢复动作（《商业级通用客户端框架总设计》§7.2：ErrorRecovery 必须提供
    /// 重试/回滚内容/清缓存/重新登录/离线模式/导出诊断/退出——本枚举冻结动作集，执行器归 C2/C3 流程）。
    /// </summary>
    public enum RecoveryAction
    {
        /// <summary>有界退避后重试当前阶段。</summary>
        Retry = 0,

        /// <summary>回滚到上一可用内容版本（热更专项：失败保留已发布版本）。</summary>
        RollbackContent = 1,

        /// <summary>清受控缓存后重试（不清账号数据）。</summary>
        ClearCacheAndRetry = 2,

        /// <summary>回到登录（Token 过期/账号态失效）。</summary>
        Relogin = 3,

        /// <summary>离线模式（产品允许时）。</summary>
        OfflineMode = 4,

        /// <summary>导出诊断并安全退出。</summary>
        ExportDiagnosticsAndExit = 5,
    }

    /// <summary>
    /// 错误恢复决策骨架：错误 → 分类 → 默认动作。分类由抛出方标记（<see cref="ClientRecoveryException"/>）；
    /// 未标记的未知异常按 <see cref="ClientErrorKind.Fatal"/> 兜底（§7.2：初始化不变量破坏 = Fatal）。
    /// 实际的恢复执行器（重试调度/回滚流程/重新登录）归 C2 主流程——本骨架只冻结"分类 → 动作"的映射。
    /// </summary>
    public static class ClientErrorRecovery
    {
        /// <summary>错误 → 默认恢复动作（§7.2 分类表逐行映射）。</summary>
        public static RecoveryAction Resolve(ClientErrorKind kind)
        {
            switch (kind)
            {
                case ClientErrorKind.Transient: return RecoveryAction.Retry;
                case ClientErrorKind.Recoverable: return RecoveryAction.ClearCacheAndRetry;
                case ClientErrorKind.Compatibility: return RecoveryAction.ExportDiagnosticsAndExit;   // 提示更新（C2 接 UI）
                case ClientErrorKind.Security: return RecoveryAction.ExportDiagnosticsAndExit;        // 不重试（§7.2）
                case ClientErrorKind.Fatal: return RecoveryAction.ExportDiagnosticsAndExit;
                default: return RecoveryAction.ExportDiagnosticsAndExit;
            }
        }

        /// <summary>从未知异常推断分类：OCE 不算错误（取消静默）；其余按 Fatal 兜底。
        /// 已标记分类的异常（<see cref="ClientRecoveryException"/>）原样透传其分类。</summary>
        public static ClientErrorKind Classify(Exception ex)
        {
            switch (ex)
            {
                case null: return ClientErrorKind.Fatal;
                case OperationCanceledException: return ClientErrorKind.Transient;   // 取消 ≠ 错误
                case ClientRecoveryException marked: return marked.Kind;
                default: return ClientErrorKind.Fatal;                               // 未知异常按 Fatal 兜底（§7.2）
            }
        }

        /// <summary>未知异常默认动作：导出诊断并安全退出（不重试——未知 = 无法分类 = 不可安全重试）。</summary>
        public static RecoveryAction ResolveUnknown(Exception ex) => Resolve(Classify(ex));
    }

    /// <summary>
    /// 带恢复分类的客户端异常：抛出方标记 <see cref="Kind"/>，恢复执行器按分类选动作
    /// （§7.2 错误分类表——分类在抛出点声明，不在 catch 点猜测）。
    /// </summary>
    public class ClientRecoveryException : Exception
    {
        public ClientErrorKind Kind { get; }

        public ClientRecoveryException(ClientErrorKind kind, string message, Exception inner = null)
            : base(message, inner)
        {
            Kind = kind;
        }
    }
}
