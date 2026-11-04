namespace LiteFramework
{
    /// <summary>恢复动作（当前能力集；Recoverable 的“清受控缓存”在执行器内升级执行，不改映射表；
    /// “重新登录”走会话路径的账号重入单点，不占启动错误动作）。</summary>
    public enum RecoveryAction
    {
        /// <summary>有界重试。</summary>
        Retry = 0,

        /// <summary>阻断（提示用户；无副作用）。</summary>
        Block = 1,

        /// <summary>导出诊断后阻断。</summary>
        ExportAndBlock = 2,

        /// <summary>导出诊断后安全退出。</summary>
        ExportAndExit = 3,
    }

    /// <summary>恢复结果。</summary>
    public enum RecoveryResult
    {
        /// <summary>重试成功。</summary>
        Recovered = 0,

        /// <summary>重试次数耗尽，仍未成功。</summary>
        Exhausted = 1,

        /// <summary>阻断（需要用户/运维动作，不自动重试）。</summary>
        Blocked = 2,

        /// <summary>致命（已导出诊断；退出动作由实现执行）。</summary>
        Fatal = 3,

        /// <summary>取消（不是错误）。</summary>
        Canceled = 4,
    }

    /// <summary>一次恢复处理的结果（错误页/诊断只消费它，不自行拼错误码）。</summary>
    public sealed class RecoveryOutcome
    {
        public RecoveryResult Result { get; private set; }

        public ErrorKind Kind { get; private set; }

        public RecoveryAction Action { get; private set; }

        public int Attempts { get; private set; }

        public string Operation { get; private set; }

        public string Message { get; private set; }

        /// <summary>是否已恢复（调用方可继续原流程）。</summary>
        public bool Recovered => Result == RecoveryResult.Recovered;

        /// <summary>是否必须阻断（错误页展示 + 不继续）。</summary>
        public bool Blocked => Result == RecoveryResult.Blocked || Result == RecoveryResult.Exhausted;

        internal static RecoveryOutcome Of(
            RecoveryResult result, ErrorKind kind, RecoveryAction action, int attempts, string operation, string message)
            => new RecoveryOutcome
            {
                Result = result,
                Kind = kind,
                Action = action,
                Attempts = attempts,
                Operation = operation,
                Message = message,
            };
    }
}
