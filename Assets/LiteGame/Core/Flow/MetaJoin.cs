namespace LiteGame
{
    /// <summary>联机进房参数（批C：Meta 签发的 Join Ticket——真实身份经服务端验签；
    /// 开发与正式构建同链，区别于 <c>ProcedureArgs.TestRoom</c> 的测试身份）。
    /// Host/Port 来自票据 Address 解析（RoomEndpoint）；Ticket 为服务端签发的不透明串。</summary>
    public readonly struct MetaJoin
    {
        public readonly string Host;
        public readonly int Port;
        public readonly string RoomId;
        public readonly string Ticket;

        public MetaJoin(string host, int port, string roomId, string ticket)
        {
            Host = host;
            Port = port;
            RoomId = roomId;
            Ticket = ticket;
        }
    }
}
