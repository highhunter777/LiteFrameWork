using System.Collections.Generic;

namespace MetaServer.Contracts.Lobby
{
    /// <summary>
    /// Lobby 稳定错误码与消息键（《Meta 服务专项设计》§5.2"错误码表归 Contracts 单源"）。
    /// 变更 = 契约变更：只增不改不重排（客户端按字符串字面量分支）。
    /// 访问令牌失败复用 <c>AuthErrorCodes.Unauthorized</c>——同一凭据类别不另立码。
    /// </summary>
    public static class LobbyErrorCodes
    {
        /// <summary>请求载荷非法（缺字段/超边界/坏 JSON）。HTTP 400。</summary>
        public const string InvalidRequest = "lobby.invalid-request";

        /// <summary>实例注册凭据无效/缺失（Lobby 实例密钥）。HTTP 401。</summary>
        public const string InstanceUnauthorized = "lobby.instance-unauthorized";

        /// <summary>Lobby 功能未配置（缺实例密钥/票据密钥——拒绝相应功能，§10）。HTTP 503。</summary>
        public const string Disabled = "lobby.disabled";

        /// <summary>无可用容量：无存活实例、实例均在 drain 或已达上报容量。HTTP 503。</summary>
        public const string NoCapacity = "lobby.no-capacity";

        /// <summary>版本不兼容：声明的 buildHash 与全部可分配实例不符（§7"版本准入"）。HTTP 409。</summary>
        public const string VersionMismatch = "lobby.version-mismatch";

        /// <summary>实例注册表容量满——**拒绝新实例**而不是淘汰旧条目（有界 + 显式拒绝）。HTTP 503。</summary>
        public const string RegistryFull = "lobby.registry-full";

        /// <summary>无效请求的消息键（本地化键空间 "meta." 前缀）。</summary>
        public const string MessageKeyInvalidRequest = "meta.lobby.invalid-request";

        /// <summary>功能不可用的消息键（disabled / no-capacity / registry-full 共用）。</summary>
        public const string MessageKeyUnavailable = "meta.lobby.unavailable";

        /// <summary>实例凭据无效的消息键。</summary>
        public const string MessageKeyUnauthorized = "meta.lobby.instance-unauthorized";

        /// <summary>版本不兼容的消息键。</summary>
        public const string MessageKeyVersionMismatch = "meta.lobby.version-mismatch";
    }

    /// <summary>
    /// 实例注册/心跳命令（RoomServer → Lobby，§7"实例注册：实例 ID、版本、房间容量、当前占用、
    /// 健康状态"；周期性上报即重复提交本命令，get-or-create 幂等键 = <see cref="InstanceId"/>）。
    /// JSON 形状（camelCase，随 Minimal API web 默认）：
    /// <c>{"instanceId":"...","buildHash":"...","address":"host:port","maxRooms":8,"roomCount":0,
    /// "maxPlayers":64,"playerCount":0,"draining":false}</c>。
    /// </summary>
    public sealed class InstanceRegisterCommand
    {
        /// <summary>实例唯一标识（1..64 UTF-8 字节；重启保持稳定——同一实例的心跳是更新，不是新登记）。</summary>
        public string InstanceId { get; set; }

        /// <summary>实例当前构建哈希（票据签发按它盖章——与房间端 Join 版本红线同值）。</summary>
        public string BuildHash { get; set; }

        /// <summary>客户端可达地址 <c>host:port</c>（分配结果下发给客户端；不参与本服务寻址）。</summary>
        public string Address { get; set; }

        /// <summary>房间容量上限（配置事实，非估算——§7"不按估算值分配"）。</summary>
        public int MaxRooms { get; set; }

        /// <summary>当前在册房间数。</summary>
        public int RoomCount { get; set; }

        /// <summary>可承载玩家上限（配置上界：max_rooms × 每房 expected_players）。</summary>
        public int MaxPlayers { get; set; }

        /// <summary>当前在局玩家数。</summary>
        public int PlayerCount { get; set; }

        /// <summary>实例是否已进入排空（true = Lobby 立即停止向其分配新对局，§7"drain 协同"）。</summary>
        public bool Draining { get; set; }
    }

    /// <summary>实例注册响应：心跳存活时限（Meta 判定"可分配"的时效——客户端心跳间隔应显著小于它）。</summary>
    public sealed class InstanceRegisterResponse
    {
        /// <summary>心跳存活时限（毫秒）：超过此时限未收到心跳即从可分配集合移除。</summary>
        public long HeartbeatTtlMs { get; set; }
    }

    /// <summary>
    /// Join Ticket 签发命令（客户端 → Lobby，C→S "Command" 语义）。
    /// JSON 形状（camelCase）：<c>{"requestId":"...","roomId":"...","buildHash":"..."}</c>。
    /// </summary>
    public sealed class JoinTicketCommand
    {
        /// <summary>请求幂等/追踪标识（1..128 UTF-8 字节；进日志与 Trace，不进 Metrics 标签）。</summary>
        public string RequestId { get; set; }

        /// <summary>
        /// 期望房间号（可空 = 用 Lobby 配置的默认房间）。Lobby 完成实例分配后按它签发票据；
        /// 空且无默认 → 拒绝（建房/入房必须有路由键）。
        /// </summary>
        public string RoomId { get; set; }

        /// <summary>
        /// 调用方声明的构建哈希（可空）。非空时 Lobby 做**版本准入前置**：与目标实例不符即拒，
        /// 避免"签发了也进不去"（房间端仍以票据内盖章值做最终比对——两处同值，非第二判据）。
        /// </summary>
        public string BuildHash { get; set; }
    }

    /// <summary>
    /// Join Ticket 签发响应（S→C 载荷）。票据本身对客户端**不透明**（只透传给房间端 Join）。
    /// JSON 形状（camelCase）：
    /// <c>{"instanceId":"...","address":"host:port","roomId":"...","ticket":"...","expiresAtMs":123}</c>。
    /// </summary>
    public sealed class JoinTicketResponse
    {
        /// <summary>被分配实例的标识（诊断/对数用）。</summary>
        public string InstanceId { get; set; }

        /// <summary>被分配实例的客户端可达地址（KCP UDP；客户端据此建连）。</summary>
        public string Address { get; set; }

        /// <summary>票据绑定的房间号（客户端 Join 时同值透传）。</summary>
        public string RoomId { get; set; }

        /// <summary>签名票据（v1 线格式；客户端不可解析不可验证——只透传）。</summary>
        public string Ticket { get; set; }

        /// <summary>过期时刻（epoch 毫秒，UTC——客户端只用于提前重取，不作判据）。</summary>
        public long ExpiresAtMs { get; set; }
    }

    /// <summary>
    /// 房间状态投影（S→C 查询载荷；§7"Match 状态投影…是上报事实的投影，不是权威"）。
    /// </summary>
    public sealed class LobbyRoomsResponse
    {
        public IReadOnlyList<LobbyRoomView> Rooms { get; set; }
    }

    /// <summary>单个实例的投影视图（字段 = 最近一次心跳上报事实 + 距上次心跳的时长）。</summary>
    public sealed class LobbyRoomView
    {
        public string InstanceId { get; set; }
        public string BuildHash { get; set; }
        public string Address { get; set; }
        public int MaxRooms { get; set; }
        public int RoomCount { get; set; }
        public int MaxPlayers { get; set; }
        public int PlayerCount { get; set; }
        public bool Draining { get; set; }

        /// <summary>距最近一次心跳的毫秒数（投影新鲜度——过期即从投影消失，不猜状态）。</summary>
        public long LastSeenAgeMs { get; set; }
    }
}
