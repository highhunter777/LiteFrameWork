using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MetaWire;

namespace LiteGame
{
    /// <summary>
    /// Meta 客户端端口（《上云测试专项设计》§2：HTTPS/JSON 四端点；Bearer = 游客登录签发的访问令牌）。
    /// 实现：<see cref="UnityMetaClient"/>（UnityWebRequest）；测试替身按同契约伪造。
    ///
    /// - **结果型返回**：不抛业务异常（取消随 OperationCanceledException 传播——调用方 Scope 取消语义不变）；
    /// - 令牌/票据原文不落日志（§11.1 同源纪律——实现与消费者都不打敏感字段）；
    /// - 端点路径/JSON 形状单源在 <see cref="MetaEndpoints"/> 与 <see cref="MetaJson"/>。
    /// </summary>
    public interface IMetaClient
    {
        /// <summary>游客一键登录（deviceId 上限与服务端 FieldLimits.ShortIdBytes 同值裁剪）。</summary>
        UniTask<MetaCall<GuestLoginInfo>> GuestLoginAsync(string deviceId, CancellationToken ct = default);

        /// <summary>按房间号换 Join Ticket（roomId 空 = 服务端缺省房——快速进入通道；版本不符 409）。</summary>
        UniTask<MetaCall<JoinTicketInfo>> RequestJoinTicketAsync(string accessToken, string roomId,
            string buildHash, CancellationToken ct = default);

        /// <summary>创建房间（服务端出唯一新房号并按号签发进房票据——客户端**不发明房号**；响应形状同
        /// <see cref="JoinTicketInfo"/>；版本不符 409、无容量 503，与进房同码表）。
        /// <paramref name="mapId"/>／<paramref name="expectedPlayers"/>（开房参数）0 = 未指定——
        /// 随票据盖章，房间端建房时按表/内核容量校验（越界拒建房）。</summary>
        UniTask<MetaCall<JoinTicketInfo>> CreateRoomAsync(string accessToken, string buildHash, int mapId = 0,
            int expectedPlayers = 0, string template = null, string gmKey = null, CancellationToken ct = default);

        /// <summary>房间列表（玩家可见投影：实例 + 房间明细——挑 roomId 换票用）。</summary>
        UniTask<MetaCall<IReadOnlyList<LobbyInstanceView>>> ListRoomsAsync(string accessToken,
            CancellationToken ct = default);

        /// <summary>最近战绩（按令牌账号；limit 上限 100，服务端 SettlementMaxQueryLimit 同源）。</summary>
        UniTask<MetaCall<IReadOnlyList<MatchRecordInfo>>> ListRecentMatchesAsync(string accessToken, int limit,
            CancellationToken ct = default);
    }
}
