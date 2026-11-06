namespace RoomServer.Application
{
    /// <summary>
    /// Join 票据验证端口（《商业级通用服务端框架总设计》§P0-6/§7；《框架先行》§5-4）。
    /// </summary>
    public interface IJoinTicketValidator
    {
        JoinPrincipal Validate(string ticket, JoinContext context);
    }
}
