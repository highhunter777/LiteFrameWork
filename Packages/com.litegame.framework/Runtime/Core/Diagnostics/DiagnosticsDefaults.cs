namespace LiteFramework
{
    /// <summary>
    /// 诊断环共享缺省（命令审计 / FSM 审计 / 日志回放三环同容量）：
    /// HUD 与控制台都按 32 条对齐展示，三处各自写 32 会静默分叉。
    /// </summary>
    public static class DiagnosticsDefaults
    {
        /// <summary>诊断环容量（最近 N 条）。</summary>
        public const int AuditRingCapacity = 32;
    }
}
