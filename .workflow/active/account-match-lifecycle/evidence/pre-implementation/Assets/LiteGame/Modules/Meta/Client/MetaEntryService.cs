using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using MetaWire;

namespace LiteGame
{
    /// <summary>
    /// 联机入口事实与纯票据边界：登录态、房间列表、取票/建房取票、战绩；不解析地址、不推进流程。
    /// </summary>
    public sealed class MetaEntryService : IRoomTicketSource, IMetaConfigFacts
    {
        private readonly IMetaClient _client;          // null = 未配置（装配面按 HasMeta 决定）
        private readonly Func<string> _deviceId;
        private readonly IdentityContext _identity;
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

        /// <summary>空串归一为 null（建房取票的默认模板/口令：未配置 = 不随请求带上）。</summary>
        private static string NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;

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

        /// <summary>按房间号换票（roomId 空 = 服务端缺省房——快速进入通道）；只返回票据事实。</summary>
        public async UniTask<MetaCall<JoinTicketInfo>> RequestJoinTicketAsync(string roomId,
            CancellationToken ct = default)
        {
            if (!IsConfigured) return NotConfigured<JoinTicketInfo>();
            if (_accessToken == null) return NotLoggedIn<JoinTicketInfo>();
            ct.ThrowIfCancellationRequested();

            MetaCall<JoinTicketInfo> call = await _client.RequestJoinTicketAsync(
                _accessToken, roomId ?? "", _identity.Handshake.BuildHash, ct);
            ct.ThrowIfCancellationRequested();
            return call;
        }

        /// <summary>按注入的默认模板/口令建房取票（服务端出号；客户端不发明房号）。</summary>
        public UniTask<MetaCall<JoinTicketInfo>> CreateRoomTicketAsync(int mapId = 0, int expectedPlayers = 0,
            CancellationToken ct = default)
            => CreateRoomTicketAsync(mapId, expectedPlayers, NullIfEmpty(_roomTemplate), NullIfEmpty(_roomGmKey), ct);

        /// <summary>按显式模板/口令建房取票（编辑器 GM 页「创建云端测试房」用）；只返回票据事实。</summary>
        public async UniTask<MetaCall<JoinTicketInfo>> CreateRoomTicketAsync(int mapId, int expectedPlayers,
            string template, string gmKey, CancellationToken ct = default)
        {
            if (!IsConfigured) return NotConfigured<JoinTicketInfo>();
            if (_accessToken == null) return NotLoggedIn<JoinTicketInfo>();
            ct.ThrowIfCancellationRequested();

            MetaCall<JoinTicketInfo> call = await _client.CreateRoomAsync(_accessToken, _identity.Handshake.BuildHash,
                mapId, expectedPlayers, template, gmKey, ct);
            ct.ThrowIfCancellationRequested();
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
