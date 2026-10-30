using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json.Nodes;
using LiteGame;
using MetaWire;
using Xunit;

namespace LiteFramework.Core.Tests.Meta
{
    /// <summary>
    /// 客户端成功响应 DTO 的**反射契约守卫**（《Meta 服务专项设计》§5.2 严格性四件之一）：
    ///
    /// - 每个 DTO 的每个公开属性必须显式归入 **必填** 或 **可选**——新增属性不分类即红；
    /// - 必填属性从样例 JSON 中删除后，解析必须判 <see cref="MetaFailureKind.InvalidResponse"/>；
    /// - 可选属性删除后仍可解析（默认值合法）。
    ///
    /// 范围 = <see cref="MetaJson"/> 消费的客户端成功响应 DTO。错误体 `MetaError` 是 best-effort
    /// （code 缺失仍可按 HTTP 状态码分支），`InstanceRegisterResponse` 归 RoomServer 侧消费，
    /// 两者不在本守卫范围。
    /// </summary>
    public sealed class MetaWireContractTests
    {
        private enum Root
        {
            GuestLogin,
            JoinTicket,
            Rooms,
            Matches,
        }

        private sealed class Case
        {
            public Type Dto;
            public string Path;
            public Root Root;
        }

        private static readonly Dictionary<string, string[]> Required = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["GuestLoginInfo"] = new[] { "AccountId", "AccessToken", "TokenType", "ExpiresAtMs" },
            ["JoinTicketInfo"] = new[] { "InstanceId", "Address", "RoomId", "Ticket", "ExpiresAtMs" },
            ["RoomsResponse"] = new[] { "Rooms" },
            ["LobbyInstanceView"] = new[] { "InstanceId", "Address" },
            ["LobbyRoomEntry"] = new[] { "RoomId" },
            ["MatchesResponse"] = new[] { "Matches" },
            ["MatchRecordInfo"] = new[] { "MatchId", "FinishedAtMs" },
        };

        private static readonly Dictionary<string, string[]> Optional = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["GuestLoginInfo"] = Array.Empty<string>(),
            ["JoinTicketInfo"] = Array.Empty<string>(),
            ["RoomsResponse"] = Array.Empty<string>(),
            ["LobbyInstanceView"] = new[]
            {
                "BuildHash", "MaxRooms", "RoomCount", "MaxPlayers", "PlayerCount", "Draining",
                "LastSeenAgeMs", "Rooms",
            },
            ["LobbyRoomEntry"] = new[] { "Phase", "Players", "ExpectedPlayers", "MapId" },
            ["MatchesResponse"] = Array.Empty<string>(),
            ["MatchRecordInfo"] = new[] { "Kills", "Deaths", "EndReason", "GameplayEndReason" },
        };

        private const string GuestSample =
            "{\"accountId\":\"acc-1\",\"accessToken\":\"tok\",\"tokenType\":\"Bearer\",\"expiresAtMs\":123}";

        private const string TicketSample =
            "{\"instanceId\":\"ins-1\",\"address\":\"127.0.0.1:17777\",\"roomId\":\"room-1\",\"ticket\":\"t\",\"expiresAtMs\":123}";

        private const string RoomsSample =
            "{\"rooms\":[{\"instanceId\":\"ins-1\",\"buildHash\":\"h\",\"address\":\"127.0.0.1:17777\"," +
            "\"maxRooms\":1,\"roomCount\":1,\"maxPlayers\":2,\"playerCount\":1,\"draining\":false," +
            "\"lastSeenAgeMs\":10,\"rooms\":[{\"roomId\":\"room-1\",\"phase\":1,\"players\":1," +
            "\"expectedPlayers\":2,\"mapId\":0}]}]}";

        private const string MatchesSample =
            "{\"matches\":[{\"matchId\":\"m-1\",\"kills\":3,\"deaths\":1,\"endReason\":1," +
            "\"gameplayEndReason\":1,\"finishedAtMs\":789}]}";

        private static MetaJson Json() => new MetaJson(new StjJsonCodec());

        private static Case[] Cases()
            => new[]
            {
                new Case { Dto = typeof(GuestLoginInfo), Path = "", Root = Root.GuestLogin },
                new Case { Dto = typeof(JoinTicketInfo), Path = "", Root = Root.JoinTicket },
                new Case { Dto = typeof(RoomsResponse), Path = "", Root = Root.Rooms },
                new Case { Dto = typeof(LobbyInstanceView), Path = "rooms/0", Root = Root.Rooms },
                new Case { Dto = typeof(LobbyRoomEntry), Path = "rooms/0/rooms/0", Root = Root.Rooms },
                new Case { Dto = typeof(MatchesResponse), Path = "", Root = Root.Matches },
                new Case { Dto = typeof(MatchRecordInfo), Path = "matches/0", Root = Root.Matches },
            };

        private static string SampleOf(Root root)
        {
            switch (root)
            {
                case Root.GuestLogin: return GuestSample;
                case Root.JoinTicket: return TicketSample;
                case Root.Rooms: return RoomsSample;
                default: return MatchesSample;
            }
        }

        private static string[] NamesOf(Dictionary<string, string[]> table, string dto)
            => table.TryGetValue(dto, out string[] names) ? names : Array.Empty<string>();

        private static bool TryParse(Root root, string json, out string reason)
        {
            MetaJson api = Json();
            switch (root)
            {
                case Root.GuestLogin:
                {
                    MetaCall<GuestLoginInfo> call = api.ParseGuestLogin(json);
                    reason = call.Ok ? null : call.Failure.Detail;
                    return call.Ok;
                }
                case Root.JoinTicket:
                {
                    MetaCall<JoinTicketInfo> call = api.ParseJoinTicket(json);
                    reason = call.Ok ? null : call.Failure.Detail;
                    return call.Ok;
                }
                case Root.Rooms:
                {
                    MetaCall<IReadOnlyList<LobbyInstanceView>> call = api.ParseRooms(json);
                    reason = call.Ok ? null : call.Failure.Detail;
                    return call.Ok;
                }
                default:
                {
                    MetaCall<IReadOnlyList<MatchRecordInfo>> call = api.ParseMatches(json);
                    reason = call.Ok ? null : call.Failure.Detail;
                    return call.Ok;
                }
            }
        }

        private static string Camel(string name)
            => string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);

        private static string RemoveProperty(string json, string path, string property)
        {
            JsonNode root = JsonNode.Parse(json);
            JsonNode node = root;
            if (!string.IsNullOrEmpty(path))
            {
                foreach (string segment in path.Split('/'))
                    node = int.TryParse(segment, out int index) ? node[index] : node[segment];
            }
            ((JsonObject)node).Remove(property);
            return root.ToJsonString();
        }

        [Fact]
        public void 每个成功响应属性必须显式归类为必填或可选()
        {
            foreach (Case c in Cases())
            {
                string[] required = NamesOf(Required, c.Dto.Name);
                string[] optional = NamesOf(Optional, c.Dto.Name);

                foreach (PropertyInfo property in c.Dto.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    bool isRequired = Array.IndexOf(required, property.Name) >= 0;
                    bool isOptional = Array.IndexOf(optional, property.Name) >= 0;
                    Assert.True(isRequired ^ isOptional,
                        c.Dto.Name + "." + property.Name + " 未分类：新增响应属性必须显式归入必填或可选");
                }

                foreach (string name in required)
                    Assert.NotNull(c.Dto.GetProperty(name));
                foreach (string name in optional)
                    Assert.NotNull(c.Dto.GetProperty(name));
            }
        }

        [Fact]
        public void 必填属性缺失_解析必须判非法()
        {
            foreach (Case c in Cases())
            {
                foreach (string name in NamesOf(Required, c.Dto.Name))
                {
                    string json = RemoveProperty(SampleOf(c.Root), c.Path, Camel(name));
                    Assert.False(TryParse(c.Root, json, out string reason),
                        c.Dto.Name + "." + name + " 缺失未被拒绝（" + reason + "）");
                }
            }
        }

        [Fact]
        public void 可选属性缺失_仍可解析()
        {
            foreach (Case c in Cases())
            {
                foreach (string name in NamesOf(Optional, c.Dto.Name))
                {
                    string json = RemoveProperty(SampleOf(c.Root), c.Path, Camel(name));
                    Assert.True(TryParse(c.Root, json, out string reason),
                        c.Dto.Name + "." + name + " 只是可选，缺失不应判非法（" + reason + "）");
                }
            }
        }
    }
}
