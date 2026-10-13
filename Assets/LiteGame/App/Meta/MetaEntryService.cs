using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteGame
{
    /// <summary>
    /// 联机入口编排件（《上云测试专项设计》§2 客户端闭环：游客登录 → 房间列表 → Join Ticket → **现有 Join 管道**）。
    ///
    /// - **登录态只住本服务**：访问令牌不落日志、不出服务面（§11.1 同源纪律）——Lua 门面只回
    ///   accountId 与结果形状（见 <c>Bridge.Meta</c>）；
    /// - **进房 = Request(Match, MetaJoin)**：走 ProcedureMatch 既有管线（JoinAck → Battle 移交不变）——
    ///   票据经验签才能进房，本服务不做第二套会话；
    /// - **零引擎依赖**：设备标识经 <c>Func&lt;string&gt;</c> 注入、流程机经 <see cref="IStageHost{TId,TReq}"/>
    ///   窄接口注入——EditMode 直测；
    /// - **fail-closed**：未配置 Meta 基址（无 -meta.url）= <see cref="IsConfigured"/> false，一切入口显性失败，
    ///   不悄悄回退本地/测试身份。
    /// </summary>
    public sealed class MetaEntryService
    {
        private readonly IMetaClient _client;          // null = 未配置（装配面按 HasMeta 决定）
        private readonly Func<string> _deviceId;
        private IStageHost<ProcedureId, ProcedureArgs> _machine;
        private string _accessToken;                    // 访问令牌（只住本服务——不出日志不出服务面）

        public MetaEntryService(IMetaClient client, Func<string> deviceId, string serverUrl)
        {
            _client = client;                           // 可空（未配置装配——IsConfigured 判据）
            _deviceId = deviceId ?? throw new ArgumentNullException(nameof(deviceId));
            ServerUrl = serverUrl ?? string.Empty;
        }

        /// <summary>是否配置了联机入口（false = 一切入口显性失败——页面据此显示"未配置服务器"）。</summary>
        public bool IsConfigured => _client != null;

        /// <summary>当前登录账号（null = 未登录——登录页/大厅据此分支）。</summary>
        public string AccountId { get; private set; }

        /// <summary>Meta 基址（设置页只读展示——来自部署注入）。</summary>
        public string ServerUrl { get; }

        /// <summary>装配点接流程机（ContainerModule 建机后回填——请求面只认窄接口）。</summary>
        public void BindMachine(IStageHost<ProcedureId, ProcedureArgs> machine)
        {
            _machine = machine ?? throw new ArgumentNullException(nameof(machine));
        }

        /// <summary>游客一键登录（成功缓存登录态——AccountId/访问令牌；重复调用即重登，服务端按 deviceId 幂等）。</summary>
        public async UniTask<MetaCall<GuestLoginInfo>> GuestLoginAsync()
        {
            if (!IsConfigured) return NotConfigured<GuestLoginInfo>();
            MetaCall<GuestLoginInfo> call = await _client.GuestLoginAsync(_deviceId());
            if (call.Ok)
            {
                AccountId = call.Value.AccountId;
                _accessToken = call.Value.AccessToken;
            }
            return call;
        }

        /// <summary>房间列表（玩家可见投影——挑 roomId 换票用）。</summary>
        public async UniTask<MetaCall<IReadOnlyList<LobbyInstanceView>>> ListRoomsAsync()
        {
            if (!IsConfigured) return NotConfigured<IReadOnlyList<LobbyInstanceView>>();
            if (_accessToken == null) return NotLoggedIn<IReadOnlyList<LobbyInstanceView>>();
            return await _client.ListRoomsAsync(_accessToken);
        }

        /// <summary>换票进房（roomId 空 = 服务端缺省房——快速进入通道）。成功 = 票据到手且
        /// <c>Request(Match, MetaJoin)</c> 已发（JoinAck 属流程机后续——对局移交与失败收尾走既有管线）。</summary>
        public async UniTask<MetaCall<JoinTicketInfo>> EnterRoomAsync(string roomId)
        {
            if (!IsConfigured) return NotConfigured<JoinTicketInfo>();
            if (_accessToken == null) return NotLoggedIn<JoinTicketInfo>();
            if (_machine == null)
                return MetaCall<JoinTicketInfo>.Failed(
                    MetaFailure.Invalid("流程机未绑定（装配面缺 BindMachine）"));

            MetaCall<JoinTicketInfo> call =
                await _client.RequestJoinTicketAsync(_accessToken, roomId ?? "", LiteNet.BuildHash.Value);
            if (!call.Ok) return call;

            if (!RoomEndpoint.TryParse(call.Value.Address, out RoomEndpoint endpoint, out string reason))
                return MetaCall<JoinTicketInfo>.Failed(
                    MetaFailure.Invalid("票据地址不可解析（" + reason + "）——联系部署侧核对 AdvertiseHost"));

            _machine.Request(ProcedureId.Match, new ProcedureArgs(metaJoin: new MetaJoin(
                endpoint.Host, endpoint.Port, call.Value.RoomId, call.Value.Ticket)));
            return call;
        }

        /// <summary>最近战绩（按登录账号；limit 走服务端上限裁剪）。</summary>
        public async UniTask<MetaCall<IReadOnlyList<MatchRecordInfo>>> ListRecordsAsync(int limit)
        {
            if (!IsConfigured) return NotConfigured<IReadOnlyList<MatchRecordInfo>>();
            if (_accessToken == null) return NotLoggedIn<IReadOnlyList<MatchRecordInfo>>();
            return await _client.ListRecentMatchesAsync(_accessToken, limit);
        }

        private static MetaCall<T> NotConfigured<T>()
            => MetaCall<T>.Failed(MetaFailure.Rejected(0, "entry.not-configured", "未配置服务器地址（-meta.url）"));

        private MetaCall<T> NotLoggedIn<T>()
            => MetaCall<T>.Failed(MetaFailure.Rejected(0, "entry.not-logged-in", "未登录"));
    }
}
