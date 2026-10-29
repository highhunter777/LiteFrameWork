using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteFramework.UI;

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
    public sealed class MetaEntryService : IAccountSessionControl
    {
        private readonly IMetaClient _client;          // null = 未配置（装配面按 HasMeta 决定）
        private readonly Func<string> _deviceId;
        private readonly IdentityContext _identity;
        private IStageHost<ProcedureId, ProcedureArgs> _machine;
        private string _accessToken;                    // 访问令牌（只住本服务——不出日志不出服务面）

        public MetaEntryService(IMetaClient client, Func<string> deviceId, string serverUrl, IdentityContext identity)
        {
            _client = client;                           // 可空（未配置装配——IsConfigured 判据）
            _deviceId = deviceId ?? throw new ArgumentNullException(nameof(deviceId));
            _identity = identity ?? throw new ArgumentNullException(nameof(identity));
            ServerUrl = serverUrl ?? string.Empty;
        }

        /// <summary>本安装包构建身份（门面版本展示与换票/建房同源）。</summary>
        public string BuildHash => _identity.Handshake.BuildHash;

        /// <summary>云端测试房模板名（服务端 `roomserver.*.json` 的 `rooms.sandbox`；显式建房用）。</summary>
        public const string TestRoomTemplate = "sandbox";

        /// <summary>云端测试房参数（开发者本机部署注入）：由装配点从 <see cref="MetaEntryConfig"/> 注入。
        /// 空模板 = 普通建房；非空则随请求带上，口令与服务端比对（不匹配 403）。</summary>
        public void ConfigureTestRoom(string template, string gmKey)
        {
            _roomTemplate = template ?? string.Empty;
            _roomGmKey = gmKey ?? string.Empty;
        }

        private string _roomTemplate = string.Empty;
        private string _roomGmKey = string.Empty;

        /// <summary>是否配置了联机入口（false = 一切入口显性失败——页面据此显示"未配置服务器"）。</summary>
        public bool IsConfigured => _client != null;

        /// <summary>当前登录账号（null = 未登录——登录页/大厅据此分支）。</summary>
        public string AccountId { get; private set; }

        /// <summary>是否已登录（令牌在手；账号重入/页面分支的判据）。</summary>
        public bool IsLoggedIn => _accessToken != null;

        /// <summary>
        /// 清登录态（账号重入单点用）：令牌/账号即弃——后续 Meta 调用一律 NotLoggedIn，
        /// 不重放旧令牌、不落令牌原文。幂等。
        /// </summary>
        public void Logout(string reason)
        {
            if (AccountId == null && _accessToken == null) return;
            AccountId = null;
            _accessToken = null;
            Log.Info($"登录态已清除（{reason}）", LogTag.Meta);
        }

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
            if (_machine == null) return MachineUnbound<JoinTicketInfo>();

            return JoinWith(await _client.RequestJoinTicketAsync(
                _accessToken, roomId ?? "", _identity.Handshake.BuildHash));
        }

        /// <summary>创建房间（**服务端出号 → 票据即达**）：调 create-room 拿唯一新号票据，随后与换票进房
        /// 同链——走 <see cref="JoinWith"/> 的 <c>Request(Match, MetaJoin)</c> 管道（客户端**不发明房号**）。
        /// <paramref name="mapId"/>（多图批）0 = 未指定（宿主默认图）；非 0 = 请求地图行键（随票据盖章，
        /// 房间端按表校验——缺行/不一致拒进房，票据/JoinAck 是权威）。</summary>
        public UniTask<MetaCall<JoinTicketInfo>> CreateRoomAsync(int mapId = 0, int expectedPlayers = 0)
            => CreateRoomAsync(mapId, expectedPlayers, NullIfEmpty(_roomTemplate), NullIfEmpty(_roomGmKey));

        /// <summary>
        /// 显式模板建房（编辑器 GM 页「创建云端测试房」用）：不依赖启动时注入的模板/口令，
        /// 直接按本次请求的 template/gmKey 建房——developer 可在同一客户端里建普通房或云端测试房。
        /// </summary>
        public async UniTask<MetaCall<JoinTicketInfo>> CreateRoomAsync(
            int mapId, int expectedPlayers, string template, string gmKey)
        {
            if (!IsConfigured) return NotConfigured<JoinTicketInfo>();
            if (_accessToken == null) return NotLoggedIn<JoinTicketInfo>();
            if (_machine == null) return MachineUnbound<JoinTicketInfo>();

            return JoinWith(await _client.CreateRoomAsync(_accessToken, _identity.Handshake.BuildHash, mapId,
                expectedPlayers, template, gmKey));
        }

        private static string NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;

        /// <summary>门票 → 进房（换票进房与创建房间的**共用尾段**——两处同为"按号签发"，防漂移）：
        /// 解析票据实例地址 → 派 <c>Request(Match, MetaJoin)</c>。失败原样返回（未到手/地址不可解析不进管道）。</summary>
        private MetaCall<JoinTicketInfo> JoinWith(MetaCall<JoinTicketInfo> call)
        {
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

        /// <summary>
        /// 离房/离局（进房的逆操作，同一窄接口同一管道）：请求流程机回 Main——在途阶段
        /// （Match 等 JoinAck / Battle 对局）被取消，资源释放归各自 finally（离场无残留的既有流程侧保证）。
        /// 未绑定流程机 = 无操作（false；与 <see cref="EnterRoomAsync"/> 的显性失败同判据）。
        /// </summary>
        public bool LeaveMatch()
        {
            if (_machine == null) return false;
            if (_machine.Current == ProcedureId.Main) return false;   // 已在 Main（无对局/工作台等 dev 落点）：
                                                                     // 请求 Main 会被流程机拒为"重入禁止"并抛——离场语义此处不成立，直接 no-op
            _machine.Request(ProcedureId.Main);
            return true;
        }

        private static MetaCall<T> NotConfigured<T>()
            => MetaCall<T>.Failed(MetaFailure.Rejected(0, "entry.not-configured", "未配置服务器地址（-meta.url）"));

        private MetaCall<T> NotLoggedIn<T>()
            => MetaCall<T>.Failed(MetaFailure.Rejected(0, "entry.not-logged-in", "未登录"));

        /// <summary>流程机未绑定（装配面缺 <c>BindMachine</c>）——进房/创建房间同判据。</summary>
        private static MetaCall<T> MachineUnbound<T>()
            => MetaCall<T>.Failed(MetaFailure.Invalid("流程机未绑定（装配面缺 BindMachine）"));
    }
}
