namespace MetaWire
{
    /// <summary>
    /// Meta 端点路径的唯一真相：客户端调用、服务端 `Map*` 挂载、房间服注册客户端都引用这里的常量——
    /// 路径写错从"运行时 404"变成"编译失败"。
    /// </summary>
    public static class MetaWireEndpoints
    {
        /// <summary>游客登录（全服务唯一不带凭据的写面，服务端有按源 IP 的兜底限流）。</summary>
        public const string GuestLogin = "/auth/guest";

        /// <summary>按房间号换 Join Ticket（roomId 空 = 服务端默认房）。</summary>
        public const string JoinTicket = "/lobby/join-ticket";

        /// <summary>创建房间（服务端出唯一新房号并按号签票）。</summary>
        public const string CreateRoom = "/lobby/create-room";

        /// <summary>房间列表（玩家可见投影）。</summary>
        public const string RoomsPublic = "/lobby/rooms/public";

        /// <summary>房间列表（全量，含未公开明细）。</summary>
        public const string RoomsAll = "/lobby/rooms";

        /// <summary>实例注册/心跳（房间服调用，实例密钥鉴权，与结算凭据分权）。</summary>
        public const string InstanceRegister = "/lobby/instances/register";

        /// <summary>结算结果提交（房间服调用，实例密钥）。</summary>
        public const string MatchesResult = "/matches/result";

        /// <summary>按账号查询战绩（玩家令牌）。</summary>
        public const string Matches = "/matches";
    }
}
