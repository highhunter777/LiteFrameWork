using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace LiteGame
{
    /// <summary>
    /// Meta JSON 单源（请求体构建 + 响应体解析 + 错误体分类）。
    /// 形状契约：服务端 <c>MetaServer.Contracts</c>（camelCase；错误体 <c>{ code, messageKey, args }</c>）。
    ///
    /// **解析失败 = <see cref="MetaFailureKind.InvalidResponse"/>**（确定错误态——2xx 但形状不对是协议/版本问题，
    /// 重试无效）；**非 2xx = <see cref="MetaFailureKind.Rejected"/>**（code 分支消费，409/401 确定性、5xx 暂态）。
    /// 本件 Newtonsoft 收口在一处——DTO（<see cref="MetaContracts"/> 件）不沾序列化器。
    /// </summary>
    internal static class MetaJson
    {
        // ---- 请求体（camelCase 与服务端 Command 属性一一对应）----

        /// <summary>游客登录请求体：{ requestId, deviceId }。</summary>
        public static string BuildGuestLoginRequest(string requestId, string deviceId)
            => "{\"requestId\":" + Quote(requestId) + ",\"deviceId\":" + Quote(deviceId) + "}";

        /// <summary>票据请求体：{ requestId, roomId, buildHash }。</summary>
        public static string BuildJoinTicketRequest(string requestId, string roomId, string buildHash)
            => "{\"requestId\":" + Quote(requestId)
               + ",\"roomId\":" + Quote(roomId)
               + ",\"buildHash\":" + Quote(buildHash) + "}";

        /// <summary>创建房间请求体：{ requestId, buildHash, mapId, expectedPlayers }（服务端出唯一新号并按号签发
        /// ——响应同票据形状，解析复用 <see cref="ParseJoinTicket"/>；客户端不发明房号）。两参数
        /// （开房参数批）0 = 未指定：mapId 按宿主默认图、expectedPlayers 按房间模板——非 0 值随票据盖章，
        /// 房间端建房时校验（地图缺行／人数越内核容量 → 拒建房）。</summary>
        public static string BuildCreateRoomRequest(string requestId, string buildHash, int mapId = 0,
            int expectedPlayers = 0, string template = null, string gmKey = null)
        {
            string body = "{\"requestId\":" + Quote(requestId)
               + ",\"buildHash\":" + Quote(buildHash)
               + ",\"mapId\":" + mapId.ToString(System.Globalization.CultureInfo.InvariantCulture)
               + ",\"expectedPlayers\":" + expectedPlayers.ToString(System.Globalization.CultureInfo.InvariantCulture);
            // 联机测试房批：空模板 = 不写字段（普通建房请求体与之前逐字节一致）
            if (!string.IsNullOrEmpty(template))
            {
                body += ",\"template\":" + Quote(template);
                if (!string.IsNullOrEmpty(gmKey)) body += ",\"gmKey\":" + Quote(gmKey);
            }
            return body + "}";
        }

        /// <summary>JSON 字符串字面量（转义控制字符/引号/反斜杠；保持请求体无内插污染）。</summary>
        private static string Quote(string value)
        {
            if (string.IsNullOrEmpty(value)) return "\"\"";
            return Newtonsoft.Json.JsonConvert.ToString(value);
        }

        // ---- 响应体（2xx → 载荷；字段缺失/类型不符 = InvalidResponse）----

        /// <summary>{ accountId, accessToken, expiresAtMs }（tokenType 现为 "Bearer"，恒定不消费）。</summary>
        public static MetaCall<GuestLoginInfo> ParseGuestLogin(string body)
        {
            JObject root = ParseObject(body, out MetaFailure failure);
            if (root == null) return MetaCall<GuestLoginInfo>.Failed(failure);

            if (!TryStr(root, "accountId", out string accountId)
                || !TryStr(root, "accessToken", out string accessToken)
                || !TryLong(root, "expiresAtMs", out long expiresAtMs))
                return MetaCall<GuestLoginInfo>.Failed(MetaFailure.Invalid("guest-login 响应缺必需字段"));

            return MetaCall<GuestLoginInfo>.OkResult(new GuestLoginInfo
            {
                AccountId = accountId,
                AccessToken = accessToken,
                ExpiresAtMs = expiresAtMs,
            });
        }

        /// <summary>{ instanceId, address, roomId, ticket, expiresAtMs }。</summary>
        public static MetaCall<JoinTicketInfo> ParseJoinTicket(string body)
        {
            JObject root = ParseObject(body, out MetaFailure failure);
            if (root == null) return MetaCall<JoinTicketInfo>.Failed(failure);

            if (!TryStr(root, "instanceId", out string instanceId)
                || !TryStr(root, "address", out string address)
                || !TryStr(root, "roomId", out string roomId)
                || !TryStr(root, "ticket", out string ticket)
                || !TryLong(root, "expiresAtMs", out long expiresAtMs))
                return MetaCall<JoinTicketInfo>.Failed(MetaFailure.Invalid("join-ticket 响应缺必需字段"));

            return MetaCall<JoinTicketInfo>.OkResult(new JoinTicketInfo
            {
                InstanceId = instanceId,
                Address = address,
                RoomId = roomId,
                Ticket = ticket,
                ExpiresAtMs = expiresAtMs,
            });
        }

        /// <summary>{ rooms:[{ instanceId, buildHash, address, playerCount, draining, rooms:[{ roomId, phase, players, expectedPlayers }] }] }。</summary>
        public static MetaCall<IReadOnlyList<LobbyInstanceView>> ParseRooms(string body)
        {
            JObject root = ParseObject(body, out MetaFailure failure);
            if (root == null) return MetaCall<IReadOnlyList<LobbyInstanceView>>.Failed(failure);

            JArray rooms = root["rooms"] as JArray;
            if (rooms == null)
                return MetaCall<IReadOnlyList<LobbyInstanceView>>.Failed(MetaFailure.Invalid("rooms 响应缺 rooms 数组"));

            var result = new List<LobbyInstanceView>(rooms.Count);
            foreach (JToken item in rooms)
            {
                if (!(item is JObject instance))
                    return MetaCall<IReadOnlyList<LobbyInstanceView>>.Failed(MetaFailure.Invalid("rooms 数组元素非对象"));
                if (!TryStr(instance, "instanceId", out string instanceId)
                    || !TryStr(instance, "address", out string address)
                    || !TryInt(instance, "playerCount", out int playerCount)
                    || !TryBool(instance, "draining", out bool draining))
                    return MetaCall<IReadOnlyList<LobbyInstanceView>>.Failed(MetaFailure.Invalid("实例行缺必需字段"));

                // 实例明细**可空**（契约：`null`/缺省 = 该实例未上报明细，旧实例兼容——服务端
                // `LobbyRoomView.Rooms` 允许 null，序列化为 `"rooms":null`）⇒ 未上报按"该实例无房"收，
                // 不判整响应非法：否则一个未上报实例毒死整张列表，客户端只能对玩家显示"加载失败"
                // （而服务端投影本身是合法的）。
                var inner = instance["rooms"] as JArray ?? new JArray();

                var entries = new List<LobbyRoomEntry>(inner.Count);
                foreach (JToken roomToken in inner)
                {
                    if (!(roomToken is JObject room))
                        return MetaCall<IReadOnlyList<LobbyInstanceView>>.Failed(MetaFailure.Invalid("房间行非对象"));
                    if (!TryStr(room, "roomId", out string roomId)
                        || !TryInt(room, "phase", out int phase)
                        || !TryInt(room, "players", out int players)
                        || !TryInt(room, "expectedPlayers", out int expectedPlayers))
                        return MetaCall<IReadOnlyList<LobbyInstanceView>>.Failed(MetaFailure.Invalid("房间行缺必需字段"));
                    entries.Add(new LobbyRoomEntry
                    {
                        RoomId = roomId,
                        Phase = phase,
                        Players = players,
                        ExpectedPlayers = expectedPlayers,
                        MapId = TryInt(room, "mapId", out int roomMapId) ? roomMapId : 0,   // 多图批（旧实例缺省 0 = 未标注）
                    });
                }

                result.Add(new LobbyInstanceView
                {
                    InstanceId = instanceId,
                    BuildHash = TryStr(instance, "buildHash", out string buildHash) ? buildHash : "",
                    Address = address,
                    PlayerCount = playerCount,
                    Draining = draining,
                    Rooms = entries,
                });
            }
            return MetaCall<IReadOnlyList<LobbyInstanceView>>.OkResult(result);
        }

        /// <summary>{ matches:[{ matchId, kills, deaths, endReason, gameplayEndReason, finishedAtMs }] }。</summary>
        public static MetaCall<IReadOnlyList<MatchRecordInfo>> ParseMatches(string body)
        {
            JObject root = ParseObject(body, out MetaFailure failure);
            if (root == null) return MetaCall<IReadOnlyList<MatchRecordInfo>>.Failed(failure);

            if (!(root["matches"] is JArray matches))
                return MetaCall<IReadOnlyList<MatchRecordInfo>>.Failed(MetaFailure.Invalid("matches 响应缺 matches 数组"));

            var result = new List<MatchRecordInfo>(matches.Count);
            foreach (JToken item in matches)
            {
                if (!(item is JObject match))
                    return MetaCall<IReadOnlyList<MatchRecordInfo>>.Failed(MetaFailure.Invalid("matches 数组元素非对象"));
                if (!TryStr(match, "matchId", out string matchId)
                    || !TryInt(match, "kills", out int kills)
                    || !TryInt(match, "deaths", out int deaths)
                    || !TryLong(match, "finishedAtMs", out long finishedAtMs))
                    return MetaCall<IReadOnlyList<MatchRecordInfo>>.Failed(MetaFailure.Invalid("战绩行缺必需字段"));
                result.Add(new MatchRecordInfo
                {
                    MatchId = matchId,
                    Kills = kills,
                    Deaths = deaths,
                    EndReason = TryInt(match, "endReason", out int endReason) ? endReason : 0,
                    GameplayEndReason = TryInt(match, "gameplayEndReason", out int gameplay) ? gameplay : 0,
                    FinishedAtMs = finishedAtMs,
                });
            }
            return MetaCall<IReadOnlyList<MatchRecordInfo>>.OkResult(result);
        }

        // ---- 错误体（非 2xx：{ code, messageKey, args }——code 分支消费，原文不回显）----

        /// <summary>把非 2xx 响应分类为 Rejected；错误体不可解析时 Code 为空串（状态码仍可分支）。</summary>
        public static MetaFailure ParseFailure(long httpStatus, string body)
        {
            JObject root = ParseObject(body, out _);
            string code = root != null && TryStr(root, "code", out string parsed) ? parsed : "";
            string messageKey = root != null && TryStr(root, "messageKey", out string key) ? key : "";
            return MetaFailure.Rejected(httpStatus, code, messageKey);
        }

        // ---- 基元取值（类型不符/缺失 = false；数值面只认整型，拒绝隐式字符串转数字）----

        private static JObject ParseObject(string body, out MetaFailure failure)
        {
            failure = null;
            if (string.IsNullOrEmpty(body))
            {
                failure = MetaFailure.Invalid("响应体为空");
                return null;
            }
            try
            {
                if (JToken.Parse(body) is JObject obj) return obj;
                failure = MetaFailure.Invalid("响应体非 JSON 对象");
                return null;
            }
            catch (Exception)
            {
                failure = MetaFailure.Invalid("响应体坏 JSON");
                return null;
            }
        }

        private static bool TryStr(JObject obj, string field, out string value)
        {
            value = null;
            if (!(obj[field] is JToken token) || token.Type != JTokenType.String) return false;
            value = (string)token;
            return !string.IsNullOrEmpty(value);
        }

        private static bool TryLong(JObject obj, string field, out long value)
        {
            value = 0;
            if (!(obj[field] is JToken token) || token.Type != JTokenType.Integer) return false;
            value = (long)token;
            return true;
        }

        private static bool TryInt(JObject obj, string field, out int value)
        {
            value = 0;
            if (!TryLong(obj, field, out long v) || v < int.MinValue || v > int.MaxValue) return false;
            value = (int)v;
            return true;
        }

        private static bool TryBool(JObject obj, string field, out bool value)
        {
            value = false;
            if (!(obj[field] is JToken token) || token.Type != JTokenType.Boolean) return false;
            value = (bool)token;
            return true;
        }
    }
}
