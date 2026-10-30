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

        /// <summary>越权：测试房口令缺失/不匹配（联机测试房批）。HTTP 403。</summary>
        public const string Forbidden = "lobby.forbidden";

        /// <summary>越权的消息键。</summary>
        public const string MessageKeyForbidden = "meta.lobby.forbidden";
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

        /// <summary>
        /// 房间明细（可选——房间列表批：缺省/空 = 该实例未上报明细，旧实例兼容）。
        /// 上限 = <see cref="MaxRooms"/>；终态房间（已销毁）不报。
        /// </summary>
        public List<RoomReport> Rooms { get; set; }
    }

    /// <summary>
    /// 单房间明细上报（RoomServer → Lobby，随心跳；投影"上报事实的投影，不是权威"）。
    /// phase = RoomRuntime.MatchPhase 数值——值域与终态判据两端同源（MetaServer.csproj 源链接共编
    /// 内核 MatchPhase/MatchStateMachine）；**终态（Closed/Aborted）不报**（读面过滤 + Meta 侧防御拒）。
    /// </summary>
    public sealed class RoomReport
    {
        /// <summary>房间号（1..64 UTF-8 字节——与进房路由键同口径）。</summary>
        public string RoomId { get; set; }

        /// <summary>对局阶段（MatchPhase 数值；列表消费者按 ≤ WaitingForPlayers 语义理解"等待中可进"）。</summary>
        public int Phase { get; set; }

        /// <summary>当前在座玩家数（已入座席位，含断线保留席）。</summary>
        public int Players { get; set; }

        /// <summary>模板期望人数（0 = 房间未配置）。</summary>
        public int ExpectedPlayers { get; set; }

        /// <summary>本房地图行键（多图批；0 = 未标注——房间列表/详情据此显示与选房，不是判定来源）。</summary>
        public int MapId { get; set; }
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

        /// <summary>请求地图行键（多图批；0 = 未指定，按房间既有图/宿主默认图）。正值盖章进票据——
        /// 房间端按表校验（缺行/未知图拒建房），既有房间图不一致则拒进房。</summary>
        public int MapId { get; set; }

        /// <summary>请求期望人数（开房参数批；0 = 未指定＝按房间模板 `expected_players`）。正值盖章进票据——
        /// 只作用于**新建房**；房间端按内核席位容量校验（越界拒建房），既有房间以房事实为准。</summary>
        public int ExpectedPlayers { get; set; }

        /// <summary>请求的房间模板名（联机测试房批；空/缺省 = 走房间服 `default_template`）。
        /// 非空时**必须**与 <see cref="GmKey"/> 一并校验（门禁单点在
        /// <c>JoinTicketIssueUseCase.Issue</c>——join-ticket 与 create-room 共用，避免旁路）。</summary>
        public string Template { get; set; }

        /// <summary>测试房口令（联机测试房批；仅当 <see cref="Template"/> 非空时校验）。
        /// 值来自机器私有配置（环境变量 `LITEGAME_GM_ROOM_KEY`），**不进仓库、不回显、不落日志**。</summary>
        public string GmKey { get; set; }
    }

    /// <summary>
    /// 创建房间命令（客户端 → Lobby，房主开局批）：客户端**不发明房号**——服务端生成
    /// 保证唯一的新号并按它签发进房票据（响应同 <see cref="JoinTicketResponse"/> 形状）。
    /// JSON 形状（camelCase）：<c>{"requestId":"...","buildHash":"..."}</c>。
    /// </summary>
    public sealed class CreateRoomCommand
    {
        /// <summary>请求幂等/追踪标识（1..128 UTF-8 字节；进日志与 Trace，不进 Metrics 标签）。</summary>
        public string RequestId { get; set; }

        /// <summary>调用方声明的构建哈希（可空；语义同 <see cref="JoinTicketCommand.BuildHash"/>——版本准入前置）。</summary>
        public string BuildHash { get; set; }

        /// <summary>请求地图行键（多图批；0 = 未指定 = 宿主默认图）。正值盖章进票据——房间端建房时按表校验。</summary>
        public int MapId { get; set; }

        /// <summary>请求期望人数（开房参数批；0 = 未指定 = 按房间模板）。正值盖章进票据——
        /// 房间端建房时按内核席位容量校验，越界拒建房。</summary>
        public int ExpectedPlayers { get; set; }

        /// <summary>请求房间模板名（联机测试房批；空 = 走 `default_template`）。**非空必须配对口令**——
        /// 口令在 <see cref="GmKey"/>，由 Meta 与机器私有配置比对；不匹配 403，且房间不创建。</summary>
        public string Template { get; set; }

        /// <summary>测试房口令（联机测试房批；仅当 <see cref="Template"/> 非空时校验）。
        /// 值来自机器私有配置（环境变量），**不进仓库、不回显、不落日志**。</summary>
        public string GmKey { get; set; }
    }




}
