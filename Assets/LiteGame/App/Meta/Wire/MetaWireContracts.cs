using System.Collections.Generic;

namespace MetaWire
{
    /// <summary>
    /// Meta HTTP 契约的**线形状**（双端唯一真相）：客户端 `LiteGame.App` 编译它，
    /// 服务端 `MetaServer.csproj` 以源链接共编（同 `JoinTicketFormat` 的既有惯例）。
    ///
    /// 纪律：
    /// <list type="bullet">
    /// <item>**只用属性**（`{ get; set; }`）——STJ 默认只序列化属性，字段会静默成空对象；</item>
    /// <item>**零序列化注解**——注解会绑定某个序列化库，而本文件必须能编进零第三方约束的位置；</item>
    /// <item>**只放线上形状**，不放客户端本地结果类型（`MetaCall`/`MetaFailure` 等留在客户端）；</item>
    /// <item>集合用 `List&lt;T&gt;`：两端反序列化器都能直接构造。</item>
    /// </list>
    ///
    /// 字段名即线名（camelCase 由序列化设置统一给出）：改名 = 改协议。
    /// </summary>
    public sealed class MetaError
    {
        /// <summary>稳定机器可读错误码（客户端按它分支，不解析自由文本）。</summary>
        public string Code { get; set; }

        /// <summary>本地化消息键（客户端按键查表）。</summary>
        public string MessageKey { get; set; }

        /// <summary>非敏感参数（字段名/上限值等；绝不放令牌、字段原文或凭据）。</summary>
        public List<string> Args { get; set; }
    }

    // ---- Auth：游客登录 ----

    /// <summary>游客登录请求：<c>{ requestId, deviceId }</c>。</summary>
    public sealed class GuestLoginRequest
    {
        public string RequestId { get; set; }
        public string DeviceId { get; set; }
    }

    /// <summary>游客登录响应：<c>{ accountId, accessToken, tokenType, expiresAtMs }</c>。
    /// 令牌对客户端不透明（只透传）；`tokenType` 恒定 "Bearer"。</summary>
    public sealed class GuestLoginInfo
    {
        public string AccountId { get; set; }
        public string AccessToken { get; set; }
        public string TokenType { get; set; }
        public long ExpiresAtMs { get; set; }
    }

    // ---- Lobby：票据 / 建房 / 房间列表 ----

    /// <summary>换票请求：<c>{ requestId, roomId, buildHash }</c>（roomId 空 = 服务端默认房）。</summary>
    public sealed class JoinTicketRequest
    {
        public string RequestId { get; set; }
        public string RoomId { get; set; }
        public string BuildHash { get; set; }
    }

    /// <summary>建房请求：<c>{ requestId, buildHash, mapId, expectedPlayers, template, gmKey }</c>。
    /// 客户端不发明房号；0 = 未指定（按服务端默认图/房间模板）。模板非空时口令必须匹配。</summary>
    public sealed class CreateRoomRequest
    {
        public string RequestId { get; set; }
        public string BuildHash { get; set; }
        public int MapId { get; set; }
        public int ExpectedPlayers { get; set; }
        public string Template { get; set; }
        public string GmKey { get; set; }
    }

    /// <summary>票据响应（换票与建房共用形状）：<c>{ instanceId, address, roomId, ticket, expiresAtMs }</c>。</summary>
    public sealed class JoinTicketInfo
    {
        public string InstanceId { get; set; }
        public string Address { get; set; }
        public string RoomId { get; set; }
        public string Ticket { get; set; }
        public long ExpiresAtMs { get; set; }
    }

    /// <summary>房间列表响应：<c>{ rooms: [ ... ] }</c>。</summary>
    public sealed class RoomsResponse
    {
        public List<LobbyInstanceView> Rooms { get; set; }
    }

    /// <summary>实例投影（服务端上报事实的投影，不是权威）。</summary>
    public sealed class LobbyInstanceView
    {
        public string InstanceId { get; set; }
        public string BuildHash { get; set; }

        /// <summary>客户端可达地址 <c>host:port</c>。</summary>
        public string Address { get; set; }

        public int MaxRooms { get; set; }
        public int RoomCount { get; set; }
        public int MaxPlayers { get; set; }
        public int PlayerCount { get; set; }
        public bool Draining { get; set; }

        /// <summary>距最近一次心跳的毫秒数（投影新鲜度）。</summary>
        public long LastSeenAgeMs { get; set; }

        /// <summary>房间明细（空/null = 该实例未上报明细——消费方按"无房"处理，不判整表非法）。</summary>
        public List<LobbyRoomEntry> Rooms { get; set; }
    }

    /// <summary>房间明细行（列表投影与实例上报共用同一形状）。</summary>
    public sealed class LobbyRoomEntry
    {
        public string RoomId { get; set; }

        /// <summary>对局阶段（内核 MatchPhase 数值；两端值域同源）。</summary>
        public int Phase { get; set; }

        public int Players { get; set; }

        /// <summary>模板期望人数（0 = 房间未配置）。</summary>
        public int ExpectedPlayers { get; set; }

        /// <summary>地图行键（0 = 未标注）。</summary>
        public int MapId { get; set; }
    }

    // ---- Profile：战绩查询 ----

    /// <summary>战绩响应：<c>{ matches: [ ... ] }</c>。</summary>
    public sealed class MatchesResponse
    {
        public List<MatchRecordInfo> Matches { get; set; }
    }

    /// <summary>单局战绩行（结束原因值域与服务端枚举同源编号）。</summary>
    public sealed class MatchRecordInfo
    {
        public string MatchId { get; set; }
        public int Kills { get; set; }
        public int Deaths { get; set; }
        public int EndReason { get; set; }
        public int GameplayEndReason { get; set; }
        public long FinishedAtMs { get; set; }
    }

    // ---- Lobby：实例注册（房间服 → Meta）----

    /// <summary>实例注册/心跳：<c>{ instanceId, buildHash, address, maxRooms, roomCount, maxPlayers,
    /// playerCount, draining, rooms }</c>。重复提交同一 instanceId = 更新（幂等键）。</summary>
    public sealed class InstanceRegisterRequest
    {
        public string InstanceId { get; set; }
        public string BuildHash { get; set; }
        public string Address { get; set; }
        public int MaxRooms { get; set; }
        public int RoomCount { get; set; }
        public int MaxPlayers { get; set; }
        public int PlayerCount { get; set; }
        public bool Draining { get; set; }

        /// <summary>房间明细（可空/空 = 未上报明细，旧实例兼容；终态房间不上报）。</summary>
        public List<LobbyRoomEntry> Rooms { get; set; }
    }

    /// <summary>实例注册响应：<c>{ heartbeatTtlMs }</c>（超过时限未收到心跳即从可分配集合移除）。</summary>
    public sealed class InstanceRegisterResponse
    {
        public long HeartbeatTtlMs { get; set; }
    }

    // ---- Profile：结算提交（房间服 → Meta，服务间线形状）----

    /// <summary>
    /// 对局结算提交（RoomServer → Meta）：一条提交同时承载
    /// 逐玩家受验证的奖励增量（账本面）与对局结果事实（归档面）。
    ///
    /// **非 sealed**：MetaServer 侧保留一个零属性重复的继承壳（服务端命名/测试引用用），
    /// 属性形状仍只在本类定义——加字段时服务端壳与 RoomServer 同时编译失败/自动获得。
    /// 客户端不生产该请求（服务间调用），但同属线形状单源。
    /// </summary>
    public class MatchResultSubmission
    {
        public string RequestId { get; set; }
        public string MatchId { get; set; }
        public long Seed { get; set; }
        public int FinalFrame { get; set; }
        public int EndReason { get; set; }
        public int GameplayEndReason { get; set; }
        public long WinnerEntityId { get; set; }

        /// <summary>逐玩家结算项（1..64 条；同一 playerId 在同一提交内只允许出现一次）。</summary>
        public List<PlayerResultEntry> Players { get; set; }
    }

    /// <summary>单玩家的结算增量项 + 结果事实（非 sealed 理由同上）。</summary>
    public class PlayerResultEntry
    {
        public string PlayerId { get; set; }
        public string AccountId { get; set; }
        public int Kills { get; set; }
        public int Deaths { get; set; }

        /// <summary>奖励增量（-1,000,000..1,000,000；首版允许负值承载惩罚语义）。</summary>
        public long Delta { get; set; }

        /// <summary>修订号模式（缺省 -1 = 追加模式；≥0 = CAS 期望，不符即 conflict）。</summary>
        public long ExpectedRevision { get; set; } = -1L;

        /// <summary>幂等键（可空 = 服务端按 <c>match:{matchId}:{playerId}</c> 确定性派生）。</summary>
        public string OperationId { get; set; }
    }
}
