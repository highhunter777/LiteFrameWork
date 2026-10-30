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
    /// 结算提交的线形状已移入共享契约 `MetaWire`（客户端/MetaServer/RoomServer 同一份）。
    ///
    /// 本类型是**服务端命名壳**：继承共享 POCO、零属性重复。保留它的唯一原因是
    /// `Tests/MetaServer.Tests` 同时引用 MetaServer 与 RoomServer 两个程序集，直接命名
    /// `MetaWire.MatchResultSubmission` 会触发 CS0433（同一源文件编进两个程序集）；
    /// 服务端 API 用本壳类型即可，属性仍只由 `MetaWire` 定义——加字段自动获得、改名自动编译失败。
    /// </summary>
    public sealed class MatchResultSubmission : MetaWire.MatchResultSubmission
    {
    }

    /// <summary>单玩家结算项的命名壳（继承共享 `MetaWire.PlayerResultEntry`，零属性重复）。</summary>
    public sealed class PlayerResultEntry : MetaWire.PlayerResultEntry
    {
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


}
