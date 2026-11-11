namespace LiteGame.Modules.Meta.Abstractions.Account
{
    /// <summary>账户域关闭原因（日志/诊断用；不参与业务分支）。</summary>
    public enum AccountCloseReason
    {
        Relogin,
        Logout,
        AuthLost,
        Shutdown
    }
}
