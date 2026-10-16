using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using UnityEngine.Networking;

namespace LiteGame
{
    /// <summary>
    /// <see cref="IMetaClient"/> 的 UnityWebRequest 实现（《上云测试专项设计》§2：HTTPS/JSON + Bearer 访问令牌）。
    ///
    /// - **非 2xx 与传输失败都不抛**：UniTask 的 <c>SendWebRequest().WithCancellation</c> 在非 2xx 时自抛
    ///   <see cref="UnityWebRequestException"/>（HttpCandidateFetcher 同款先例），本类在此统一转
    ///   <see cref="MetaCall{T}"/>；取消（<see cref="OperationCanceledException"/>）原样传播——Scope 语义不变；
    /// - **Content-Type 必须显式 application/json**（服务端 <c>TryReadJsonAsync</c> 对非 JSON 类型按坏请求收口，
    ///   UnityWebRequest.Post(string,string) 默认表单编码——不可用，须手工 UploadHandler）；
    /// - 令牌/票据不落日志（§11.1）；失败 Detail 只带状态码与错误码，不带原文。
    /// </summary>
    public sealed class UnityMetaClient : IMetaClient
    {
        private readonly string _baseUrl;
        private readonly int _timeoutSeconds;
        private readonly Func<string> _requestIdFactory;

        /// <param name="baseUrl">Meta 基址（末尾不带 /；空 = 抛——装配面 fail-fast，联机入口缺配置在上游显性拒绝）。</param>
        /// <param name="timeoutSeconds">单请求超时（秒；&lt;1 抛）。</param>
        /// <param name="requestIdFactory">请求标识工厂（缺省时间戳+随机；注入便于测试对数）。</param>
        public UnityMetaClient(string baseUrl, int timeoutSeconds, Func<string> requestIdFactory = null)
        {
            if (string.IsNullOrEmpty(baseUrl)) throw new ArgumentException("Meta 基址必填", nameof(baseUrl));
            if (timeoutSeconds < 1) throw new ArgumentOutOfRangeException(nameof(timeoutSeconds), "超时必须 ≥ 1 秒");
            _baseUrl = baseUrl.TrimEnd('/');
            _timeoutSeconds = timeoutSeconds;
            _requestIdFactory = requestIdFactory ?? DefaultRequestId;
        }

        public async UniTask<MetaCall<GuestLoginInfo>> GuestLoginAsync(string deviceId, CancellationToken ct = default)
        {
            string body = MetaJson.BuildGuestLoginRequest(_requestIdFactory(), deviceId ?? "");
            return await SendAsync("POST", MetaEndpoints.GuestLogin, body, bearer: null,
                MetaJson.ParseGuestLogin, ct);
        }

        public async UniTask<MetaCall<JoinTicketInfo>> RequestJoinTicketAsync(string accessToken, string roomId,
            string buildHash, CancellationToken ct = default)
        {
            string body = MetaJson.BuildJoinTicketRequest(_requestIdFactory(), roomId ?? "", buildHash ?? "");
            return await SendAsync("POST", MetaEndpoints.JoinTicket, body, accessToken, MetaJson.ParseJoinTicket, ct);
        }

        public UniTask<MetaCall<JoinTicketInfo>> CreateRoomAsync(string accessToken, string buildHash, int mapId = 0,
            int expectedPlayers = 0, CancellationToken ct = default)
        {
            string body = MetaJson.BuildCreateRoomRequest(_requestIdFactory(), buildHash ?? "", mapId, expectedPlayers);
            return SendAsync("POST", MetaEndpoints.CreateRoom, body, accessToken, MetaJson.ParseJoinTicket, ct);
        }

        public UniTask<MetaCall<IReadOnlyList<LobbyInstanceView>>> ListRoomsAsync(string accessToken,
            CancellationToken ct = default)
            => SendAsync("GET", MetaEndpoints.RoomsPublic, null, accessToken, MetaJson.ParseRooms, ct);

        public UniTask<MetaCall<IReadOnlyList<MatchRecordInfo>>> ListRecentMatchesAsync(string accessToken, int limit,
            CancellationToken ct = default)
        {
            string path = limit > 0 ? MetaEndpoints.Matches + "?limit=" + limit : MetaEndpoints.Matches;
            return SendAsync("GET", path, null, accessToken, MetaJson.ParseMatches, ct);
        }

        /// <summary>统一收发：传输 → 2xx 解析载荷 / 非 2xx 解析错误体 / 网络错分类。取消原样传播。</summary>
        private async UniTask<MetaCall<T>> SendAsync<T>(string verb, string path, string jsonBody, string bearer,
            Func<string, MetaCall<T>> parse, CancellationToken ct)
        {
            using (var request = new UnityWebRequest(_baseUrl + path, verb))
            {
                if (jsonBody != null)
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = _timeoutSeconds;
                request.SetRequestHeader("Content-Type", "application/json");
                if (!string.IsNullOrEmpty(bearer))
                    request.SetRequestHeader("Authorization", "Bearer " + bearer);

                string body;
                try
                {
                    await request.SendWebRequest().WithCancellation(ct);
                    body = request.downloadHandler?.text;
                }
                catch (OperationCanceledException)
                {
                    throw;                                   // Scope 取消语义：调用方收尾，不算失败
                }
                catch (UnityWebRequestException ex)
                {
                    // 非 2xx 在这里自抛（先例同 HttpCandidateFetcher）；无响应（连接失败/DNS/超时）ResponseCode=0 → 暂态
                    if (ex.ResponseCode <= 0)
                        return MetaCall<T>.Failed(MetaFailure.Network("meta:" + path));
                    return MetaCall<T>.Failed(MetaJson.ParseFailure(ex.ResponseCode, ex.Text));
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    long code = request.responseCode;
                    if (code <= 0)
                        return MetaCall<T>.Failed(MetaFailure.Network("meta:" + path));
                    return MetaCall<T>.Failed(MetaJson.ParseFailure(code, body));
                }

                MetaCall<T> parsed = parse(body);
                return parsed;
            }
        }

        /// <summary>缺省请求标识：时间戳基 + 随机后缀（仅请求对数用途，不作身份）。</summary>
        private static string DefaultRequestId()
            => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString("x")
               + "-" + UnityEngine.Random.Range(0, 1_000_000);
    }
}
