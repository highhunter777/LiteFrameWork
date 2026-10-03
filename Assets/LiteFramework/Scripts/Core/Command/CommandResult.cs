namespace LiteFramework
{
    /// <summary>命令拒绝原因（《命令中心专项设计》§2）：审计与诊断的归因面。</summary>
    public enum CommandReject
    {
        /// <summary>成功/无拒绝。</summary>
        None = 0,
        /// <summary>GM 权限门拦截（§3：三宏之外 release 的运行态拒绝——能力隔离，不抛）。</summary>
        GmBlocked,
        /// <summary>处理器主动拒绝（Execute 返回失败）。</summary>
        HandlerRejected,
        /// <summary>处理器异常（作者代码缺陷——隔离为 Failed 结果，不炸调用方）。</summary>
        HandlerFault,
        /// <summary>拦截器链否决（§4：任一拒绝即终止）。</summary>
        InterceptorRejected,
    }

    /// <summary>
    /// 命令执行结果（《命令中心专项设计》§2）：校验/执行失败 = Ok false + Reason/Detail，
    /// 失败走结果不走异常（GmBlocked 是 release 正常路径；无处理器才是编程错误直接抛）。
    /// </summary>
    public readonly struct CommandResult
    {
        public readonly bool Ok;
        public readonly CommandReject Reason;
        public readonly string Detail;

        /// <summary>成功结果（Detail 可选描述——审计与 GM 面板回显用）。</summary>
        public static CommandResult Success(string detail = null)
            => new CommandResult(true, CommandReject.None, detail);

        /// <summary>失败结果（处理器主动拒绝/权限门拦截）。</summary>
        public static CommandResult Fail(CommandReject reason, string detail = null)
            => new CommandResult(false, reason, detail);

        public CommandResult(bool ok, CommandReject reason, string detail)
        {
            Ok = ok; Reason = reason; Detail = detail;
        }
    }
}
