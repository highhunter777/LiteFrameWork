using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MetaWire;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace LiteGameNext.Platform.Meta
{
    /// <summary>Meta HTTP 客户端：端点/JSON/失败分类集中；业务失败返回结果，不抛异常。</summary>
    public sealed class MetaClient
    {
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore,
        };

        private readonly IMetaTransport _transport;
        private readonly Func<string> _requestIdFactory;

        public MetaClient(IMetaTransport transport, Func<string> requestIdFactory = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _requestIdFactory = requestIdFactory ?? (() => Guid.NewGuid().ToString("N"));
        }

        /// <summary>游客登录；成功值是令牌/账号事实。</summary>
        public UniTask<MetaCallResult<GuestLoginInfo>> GuestLoginAsync(string deviceId,
            CancellationToken cancellationToken = default)
        {
            var body = new GuestLoginRequest
            {
                RequestId = _requestIdFactory(),
                DeviceId = MetaWireLimits.TrimUtf8(deviceId, MetaWireLimits.ShortIdBytes),
            };
            return SendAsync<GuestLoginInfo>("POST", MetaWireEndpoints.GuestLogin, null, body, cancellationToken);
        }

        /// <summary>建房：返回 JoinTicketInfo（含房号/地址/票据）。</summary>
        public UniTask<MetaCallResult<JoinTicketInfo>> CreateRoomAsync(string token, string buildHash,
            int mapId = 0, int expectedPlayers = 0, string template = null,
            CancellationToken cancellationToken = default)
        {
            bool hasTemplate = !string.IsNullOrEmpty(template);
            var body = new CreateRoomRequest
            {
                RequestId = _requestIdFactory(),
                BuildHash = buildHash ?? string.Empty,
                MapId = mapId,
                ExpectedPlayers = expectedPlayers,
                Template = hasTemplate ? template : null,
                GmKey = hasTemplate ? string.Empty : null,
            };
            return SendAsync<JoinTicketInfo>("POST", MetaWireEndpoints.CreateRoom, token, body, cancellationToken);
        }

        /// <summary>公开房间列表：响应信封 { rooms } 归一为裸列表。</summary>
        public async UniTask<MetaCallResult<IReadOnlyList<LobbyInstanceView>>> ListRoomsAsync(string token,
            CancellationToken cancellationToken = default)
        {
            MetaCallResult<RoomsResponse> result = await SendAsync<RoomsResponse>(
                "GET", MetaWireEndpoints.RoomsPublic, token, null, cancellationToken);
            if (!result.Success)
                return MetaCallResult<IReadOnlyList<LobbyInstanceView>>.Failed(result.Error);
            List<LobbyInstanceView> rooms = result.Value.Rooms ?? new List<LobbyInstanceView>();
            for (int index = 0; index < rooms.Count; index++)
                if (rooms[index].Rooms == null) rooms[index].Rooms = new List<LobbyRoomEntry>();
            return MetaCallResult<IReadOnlyList<LobbyInstanceView>>.Ok(rooms);
        }

        /// <summary>按房号换 Join 票据（roomId 空 = 服务端默认房）。</summary>
        public UniTask<MetaCallResult<JoinTicketInfo>> RequestJoinTicketAsync(string token, string roomId,
            string buildHash, CancellationToken cancellationToken = default)
        {
            var body = new JoinTicketRequest
            {
                RequestId = _requestIdFactory(),
                RoomId = roomId ?? string.Empty,
                BuildHash = buildHash ?? string.Empty,
            };
            return SendAsync<JoinTicketInfo>("POST", MetaWireEndpoints.JoinTicket, token, body, cancellationToken);
        }

        /// <summary>最近战绩：响应信封 { matches } 归一为裸列表。</summary>
        public async UniTask<MetaCallResult<IReadOnlyList<MatchRecordInfo>>> ListRecentMatchesAsync(string token,
            int limit, CancellationToken cancellationToken = default)
        {
            string path = limit > 0 ? MetaWireEndpoints.Matches + "?limit=" + limit : MetaWireEndpoints.Matches;
            MetaCallResult<MatchesResponse> result = await SendAsync<MatchesResponse>(
                "GET", path, token, null, cancellationToken);
            if (!result.Success)
                return MetaCallResult<IReadOnlyList<MatchRecordInfo>>.Failed(result.Error);
            return MetaCallResult<IReadOnlyList<MatchRecordInfo>>.Ok(
                result.Value.Matches ?? new List<MatchRecordInfo>());
        }

        private async UniTask<MetaCallResult<T>> SendAsync<T>(string method, string path, string bearer,
            object body, CancellationToken cancellationToken)
        {
            string json = body == null ? null : JsonConvert.SerializeObject(body, JsonSettings);
            MetaResponse response;
            try
            {
                response = await _transport.SendAsync(new MetaRequest(method, path, bearer, json), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Fail<T>(MetaFailureKind.Network, 0, "transport", ex.Message);
            }

            if (response.StatusCode <= 0)
                return Fail<T>(MetaFailureKind.Network, 0, "network", response.Body);
            if (response.StatusCode < 200 || response.StatusCode >= 300)
            {
                MetaWire.MetaError error = TryParseError(response.Body);
                return Fail<T>(MetaFailureKind.Http, response.StatusCode,
                    error == null ? string.Empty : error.Code,
                    error == null ? string.Empty : error.MessageKey);
            }

            if (string.IsNullOrEmpty(response.Body))
                return Fail<T>(MetaFailureKind.Protocol, response.StatusCode, "empty", "响应体为空");
            try
            {
                T value = JsonConvert.DeserializeObject<T>(response.Body, JsonSettings);
                if (value == null)
                    return Fail<T>(MetaFailureKind.Protocol, response.StatusCode, "shape", "响应为空对象");
                return MetaCallResult<T>.Ok(value);
            }
            catch (JsonException ex)
            {
                return Fail<T>(MetaFailureKind.Protocol, response.StatusCode, "shape", ex.Message);
            }
        }

        private static MetaWire.MetaError TryParseError(string body)
        {
            if (string.IsNullOrEmpty(body)) return null;
            try
            {
                return JsonConvert.DeserializeObject<MetaWire.MetaError>(body, JsonSettings);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static MetaCallResult<T> Fail<T>(MetaFailureKind kind, long status, string code, string message)
            => MetaCallResult<T>.Failed(new MetaError(kind, status, code, message));
    }
}
