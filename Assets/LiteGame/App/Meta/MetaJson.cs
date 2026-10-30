using System;
using System.Collections.Generic;
using LiteFramework;
using MetaWire;

namespace LiteGame
{
    /// <summary>
    /// Meta 契约编解码单源（请求构造 + 响应解析 + 错误体分类）。
    ///
    /// 形状来自共享契约 <c>MetaWire</c>（双端同一份 DTO），序列化行为来自 <see cref="IJsonCodec"/>
    /// （实现共用 `WireJsonSettings`）。因此本类**不再手写逐字段取值**：只做
    /// "反序列化 → 必填校验 → 失败分类" 三步——字段覆盖由共享类型保证，缺字段由校验与
    /// `MetaWireContractTests` 的“必填/可选归类 + 必填缺失”反射断言保证。
    ///
    /// 失败分类语义不变：传输层失败 = Network（暂态）；非 2xx = Rejected（按 code 分支）；
    /// 2xx 但形状/必填不符 = InvalidResponse（确定错误，不重试）。
    /// </summary>
    internal sealed class MetaJson
    {
        private readonly IJsonCodec _codec;

        /// <summary>构造（端口注入）：缺省即 fail-fast，解析器不设"静默无实现"形态。</summary>
        internal MetaJson(IJsonCodec codec)
            => _codec = codec ?? throw new ArgumentNullException(nameof(codec));

        // ---- 请求体（形状 = 共享 DTO；字段名 camelCase 由契约设置统一给出）----

        public string BuildGuestLoginRequest(string requestId, string deviceId)
            => _codec.Serialize(new GuestLoginRequest
            {
                RequestId = requestId ?? "",
                // 服务端按 UTF-8 字节校验短标识；客户端先按同一上限裁剪，避免长设备号整单 400。
                DeviceId = MetaWireLimits.TrimUtf8(deviceId, MetaWireLimits.ShortIdBytes),
            });

        public string BuildJoinTicketRequest(string requestId, string roomId, string buildHash)
            => _codec.Serialize(new JoinTicketRequest
            {
                RequestId = requestId ?? "",
                RoomId = roomId ?? "",
                BuildHash = buildHash ?? "",
            });

        /// <summary>建房请求：模板为空 = 不携带模板/口令（等价于按服务端默认模板建房）。</summary>
        public string BuildCreateRoomRequest(string requestId, string buildHash, int mapId = 0,
            int expectedPlayers = 0, string template = null, string gmKey = null)
        {
            bool hasTemplate = !string.IsNullOrEmpty(template);
            return _codec.Serialize(new CreateRoomRequest
            {
                RequestId = requestId ?? "",
                BuildHash = buildHash ?? "",
                MapId = mapId,
                ExpectedPlayers = expectedPlayers,
                Template = hasTemplate ? template : null,
                GmKey = hasTemplate ? (gmKey ?? "") : null,
            });
        }

        // ---- 响应体（2xx → 载荷；反序列化失败或必填缺失 = InvalidResponse）----

        public MetaCall<GuestLoginInfo> ParseGuestLogin(string body)
            => Parse<GuestLoginInfo>(body, MetaWireValidator.TryRequire);

        public MetaCall<JoinTicketInfo> ParseJoinTicket(string body)
            => Parse<JoinTicketInfo>(body, MetaWireValidator.TryRequire);

        /// <summary>房间列表：响应是 `{rooms:[...]}` 信封，对外仍是裸列表（既有接口面不变）。
        /// 实例未上报明细（null）归一为空列表——"无房"与"没有该字段"对消费方同义。</summary>
        public MetaCall<IReadOnlyList<LobbyInstanceView>> ParseRooms(string body)
        {
            MetaCall<RoomsResponse> parsed = Parse<RoomsResponse>(body, MetaWireValidator.TryRequire);
            if (!parsed.Ok)
                return MetaCall<IReadOnlyList<LobbyInstanceView>>.Failed(parsed.Failure);

            List<LobbyInstanceView> rooms = parsed.Value.Rooms;
            for (int i = 0; i < rooms.Count; i++)
            {
                if (rooms[i].Rooms == null) rooms[i].Rooms = new List<LobbyRoomEntry>();
            }
            return MetaCall<IReadOnlyList<LobbyInstanceView>>.OkResult(MetaWireCollections.OrEmpty(rooms));
        }

        /// <summary>战绩列表：响应是 `{matches:[...]}` 信封，对外仍是裸列表。</summary>
        public MetaCall<IReadOnlyList<MatchRecordInfo>> ParseMatches(string body)
        {
            MetaCall<MatchesResponse> parsed = Parse<MatchesResponse>(body, MetaWireValidator.TryRequire);
            return parsed.Ok
                ? MetaCall<IReadOnlyList<MatchRecordInfo>>.OkResult(
                    MetaWireCollections.OrEmpty(parsed.Value.Matches))
                : MetaCall<IReadOnlyList<MatchRecordInfo>>.Failed(parsed.Failure);
        }

        // ---- 错误体（非 2xx：{ code, messageKey, args }——code 分支消费，原文不回显）----

        /// <summary>把非 2xx 响应分类为 Rejected；错误体不可解析时 Code 为空串（状态码仍可分支）。</summary>
        public MetaFailure ParseFailure(long httpStatus, string body)
        {
            if (_codec.TryDeserialize<MetaError>(body, out MetaError error) && !string.IsNullOrEmpty(error.Code))
                return MetaFailure.Rejected(httpStatus, error.Code, error.MessageKey);
            return MetaFailure.Rejected(httpStatus, "", "");
        }

        /// <summary>通用解析：反序列化 → 必填校验 → 结果包装（失败一律 InvalidResponse）。</summary>
        private MetaCall<T> Parse<T>(string body, MetaWireRequire<T> require) where T : class
        {
            if (string.IsNullOrEmpty(body))
                return MetaCall<T>.Failed(MetaFailure.Invalid("响应体为空"));
            if (!_codec.TryDeserialize(body, out T value))
                return MetaCall<T>.Failed(MetaFailure.Invalid("响应形状不符"));
            if (!require(value, out string reason))
                return MetaCall<T>.Failed(MetaFailure.Invalid(reason));
            return MetaCall<T>.OkResult(value);
        }
    }
}
