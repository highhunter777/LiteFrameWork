using System.Collections.Generic;

namespace MetaServer.Contracts.Profile
{
    /// <summary>
    /// Profile 稳定错误码与消息键（《Meta 服务专项设计》§5.2"错误码表归 Contracts 单源"）。
    /// 变更 = 契约变更：只增不改不重排（调用方按字符串字面量分支）。
    /// </summary>
    public static class ProfileErrorCodes
    {
        /// <summary>请求载荷非法（缺字段/超边界/坏 JSON/同局重复玩家）。HTTP 400。</summary>
        public const string InvalidRequest = "profile.invalid-request";

        /// <summary>实例凭据无效/缺失（RoomServer → Meta 的服务间共享密钥）。HTTP 401。</summary>
        public const string InstanceUnauthorized = "profile.instance-unauthorized";

        /// <summary>Profile 结算功能未配置（缺存储——拒绝相应功能，§10）。HTTP 503。</summary>
        public const string Disabled = "profile.disabled";

        /// <summary>存储未确认（提交失败/响应丢失——已应用也可能没有）。HTTP 503；重试安全：
        /// 已落账的玩家项重试时 duplicate 命中首次结果，不重复发奖（§8.2）。</summary>
        public const string StoreUnconfirmed = "profile.store-unconfirmed";

        /// <summary>无效请求的消息键（本地化键空间 "meta." 前缀）。</summary>
        public const string MessageKeyInvalidRequest = "meta.profile.invalid-request";

        /// <summary>实例凭据无效的消息键。</summary>
        public const string MessageKeyUnauthorized = "meta.profile.instance-unauthorized";

        /// <summary>功能不可用（disabled / store-unconfirmed 共用）的消息键。</summary>
        public const string MessageKeyUnavailable = "meta.profile.unavailable";
    }

    /// <summary>
    /// 对局结算提交（RoomServer → Profile，§8.2 结算链的 Meta 侧入口；服务端总设计 §11.3）：
    /// **一条提交同时承载两个面**——
    /// - 逐玩家**受验证的 RewardDelta**（账本面：Meta 只接受增量，§11.2"不接受客户端上报奖励数量"；
    ///   击杀/比分等局内事实的奖励换算在提交方完成，不在本端点重建）；
    /// - 对局结果事实（归档面：击杀/死亡/结束原因——《上云测试》§4 裁决点①③"按账号可查"）。
    ///
    /// JSON 形状（camelCase，随 Minimal API web 默认）：
    /// <c>{"requestId":"...","matchId":"...","seed":0,"finalFrame":0,"endReason":0,"gameplayEndReason":0,
    /// "winnerEntityId":0,"players":[{"playerId":"...","accountId":"...","kills":0,"deaths":0,"delta":0,
    /// "expectedRevision":-1,"operationId":"..."}]}</c>。
    /// </summary>
    public sealed class MatchResultSubmission
    {
        /// <summary>请求幂等/追踪标识（1..128 UTF-8 字节；进日志与 Trace，不进 Metrics 标签）。</summary>
        public string RequestId { get; set; }

        /// <summary>对局 Id（1..64 UTF-8 字节）——逐玩家业务键 (playerId, matchId, "match") 的成员；
        /// 归档面幂等键（一局一条）。</summary>
        public string MatchId { get; set; }

        /// <summary>对局种子（归档事实；≥0）。</summary>
        public long Seed { get; set; }

        /// <summary>终局帧号（归档事实；≥0）。</summary>
        public int FinalFrame { get; set; }

        /// <summary>结束原因（<c>ShutdownReason</c> 的 int 值；≥0）。</summary>
        public int EndReason { get; set; }

        /// <summary>玩法结束原因（<c>MatchEndReason</c> 的 int 值；≥0）。</summary>
        public int GameplayEndReason { get; set; }

        /// <summary>胜者实体 Id（0 = 无；≥0）。</summary>
        public long WinnerEntityId { get; set; }

        /// <summary>逐玩家结算项（1..64 条；同一 playerId 在同一提交内只允许出现一次）。</summary>
        public List<PlayerResultEntry> Players { get; set; }
    }

    /// <summary>单玩家的结算增量项 + 结果事实。</summary>
    public sealed class PlayerResultEntry
    {
        /// <summary>玩家/席位标识（1..64 UTF-8 字节）。</summary>
        public string PlayerId { get; set; }

        /// <summary>账号标识（1..64 UTF-8 字节）——归档按账号可查的归属键（裁决点①：随载荷携带）。</summary>
        public string AccountId { get; set; }

        /// <summary>击杀数（归档事实；≥0）。</summary>
        public int Kills { get; set; }

        /// <summary>死亡数（归档事实；≥0）。</summary>
        public int Deaths { get; set; }

        /// <summary>奖励增量（-1,000,000..1,000,000；首版允许负值承载惩罚语义，范围卡住异常载荷）。</summary>
        public long Delta { get; set; }

        /// <summary>修订号模式（缺省 -1 = **追加模式**：不做 CAS 期望——结算提交方不持有玩家修订号，
        /// 幂等由账目唯一索引承载；≥0 = CAS 期望，不符即 conflict 供调用方重试）。</summary>
        public long ExpectedRevision { get; set; } = -1L;

        /// <summary>幂等键（可空 = 服务端按 <c>match:{matchId}:{playerId}</c> 确定性派生——
        /// 同局重投（Outbox 压实后重枚举/响应丢失重试）恒命中同账目；显式供给时以供给值为准）。</summary>
        public string OperationId { get; set; }
    }

    /// <summary>
    /// 结算应用响应（S→C 载荷；§8.2"返回原结果或首次结果"——duplicate 项携带的是**首次**的
    /// 修订号/余额快照，不重新计算）。任一玩家 conflict 时整响应 409（已 applied/duplicate 的
    /// 项照常携带——调用方整包重试时已落账项 duplicate 命中，收敛无重复发奖）。
    /// </summary>
    public sealed class MatchSettlementResponse
    {
        public string MatchId { get; set; }

        /// <summary>归档面幂等命中（该局结果已在库——重复提交；账本面重复另见逐玩家 duplicate 项）。</summary>
        public bool Duplicate { get; set; }

        public List<PlayerSettlementOutcome> Results { get; set; }
    }

    /// <summary>单玩家结算结果。</summary>
    public sealed class PlayerSettlementOutcome
    {
        public string PlayerId { get; set; }

        /// <summary>结果分类（稳定字符串）：<c>applied</c> 首次落账 / <c>duplicate</c> 重复提交
        /// （携带首次快照）/ <c>conflict</c> 修订号 CAS 不符（携带期望/实际）。</summary>
        public string Outcome { get; set; }

        public string OperationId { get; set; }

        /// <summary>应用后的修订号（duplicate 项 = 首次应用时的修订号）。</summary>
        public long AppliedRevision { get; set; }

        /// <summary>应用后的余额（duplicate 项 = 首次应用后的余额快照）。</summary>
        public long BalanceAfter { get; set; }

        /// <summary>CAS 期望值（仅 conflict 项有意义）。</summary>
        public long Expected { get; set; }

        /// <summary>实际修订号（仅 conflict 项有意义）。</summary>
        public long Actual { get; set; }
    }

    /// <summary>结果分类常量（稳定字符串——调用方按字面量分支）。</summary>
    public static class SettlementOutcomes
    {
        public const string Applied = "applied";
        public const string Duplicate = "duplicate";
        public const string Conflict = "conflict";
    }

    /// <summary>
    /// 结果查询响应（S→C；《上云测试》§4 裁决点③"按 accountId 列最近 N 局"）。
    /// JSON 形状（camelCase）：
    /// <c>{"matches":[{"matchId":"...","kills":3,"deaths":1,"endReason":0,"gameplayEndReason":2,"finishedAtMs":123}]}</c>。
    /// </summary>
    public sealed class MatchQueryResponse
    {
        public List<MatchSummaryView> Matches { get; set; }
    }

    /// <summary>单局账号维度摘要（查询视图；完成时刻倒序）。</summary>
    public sealed class MatchSummaryView
    {
        public string MatchId { get; set; }
        public int Kills { get; set; }
        public int Deaths { get; set; }

        /// <summary>结束原因（<c>ShutdownReason</c> 的 int 值——权威字段，不在此重定义枚举）。</summary>
        public int EndReason { get; set; }

        /// <summary>玩法结束原因（<c>MatchEndReason</c> 的 int 值）。</summary>
        public int GameplayEndReason { get; set; }

        /// <summary>完成时刻（epoch 毫秒，UTC——存储端赋值）。</summary>
        public long FinishedAtMs { get; set; }
    }
}
