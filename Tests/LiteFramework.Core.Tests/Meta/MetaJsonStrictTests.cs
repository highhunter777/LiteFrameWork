using System.Collections.Generic;
using LiteGame;
using MetaWire;
using Xunit;

namespace LiteFramework.Core.Tests.Meta
{
    /// <summary>
    /// Meta 契约的 L1 直测（零引擎零第三方）：反射反序列化 + 必填校验 + 失败分类。
    ///
    /// 钉住的性质：
    /// <list type="bullet">
    /// <item>2xx 但缺必填字段 / 根不是对象 / 坏 JSON → <see cref="MetaFailureKind.InvalidResponse"/>；</item>
    /// <item>非 2xx → Rejected 且状态码与 code 可分支；</item>
    /// <item>请求体由共享 DTO 序列化而来（字段名口径与两端一致），特殊字符不破坏 JSON。</item>
    /// </list>
    /// 类型转换的宽松性不再是本层的判据（形状由共享 DTO 在编译期单源；未知字段由
    /// `MissingMemberHandling.Error` 在运行时拦截，见 EditMode 用例）。
    /// </summary>
    public sealed class MetaJsonStrictTests
    {
        private static MetaJson Json() => new MetaJson(new StjJsonCodec());

        [Fact]
        public void 登录响应_字段完整_解析成功()
        {
            MetaCall<GuestLoginInfo> call = Json().ParseGuestLogin(
                "{\"accountId\":\"acc-1\",\"accessToken\":\"tok\",\"tokenType\":\"Bearer\",\"expiresAtMs\":123}");

            Assert.True(call.Ok);
            Assert.Equal("acc-1", call.Value.AccountId);
            Assert.Equal("tok", call.Value.AccessToken);
            Assert.Equal(123, call.Value.ExpiresAtMs);
        }

        [Fact]
        public void 登录响应_缺必填字段_判非法()
        {
            MetaCall<GuestLoginInfo> call = Json().ParseGuestLogin("{\"accountId\":\"acc-1\"}");

            Assert.False(call.Ok);
            Assert.Equal(MetaFailureKind.InvalidResponse, call.Failure.Kind);
        }

        [Fact]
        public void 登录响应_expiresAtMs缺失_判非法()
        {
            MetaCall<GuestLoginInfo> call = Json().ParseGuestLogin(
                "{\"accountId\":\"acc-1\",\"accessToken\":\"tok\",\"tokenType\":\"Bearer\"}");

            Assert.False(call.Ok);
            Assert.Equal(MetaFailureKind.InvalidResponse, call.Failure.Kind);
        }

        [Fact]
        public void 响应根不是对象_判非法()
        {
            MetaCall<GuestLoginInfo> call = Json().ParseGuestLogin("[]");

            Assert.False(call.Ok);
            Assert.Equal(MetaFailureKind.InvalidResponse, call.Failure.Kind);
        }

        [Fact]
        public void 响应是坏JSON_判非法()
        {
            MetaCall<GuestLoginInfo> call = Json().ParseGuestLogin("{ not json");

            Assert.False(call.Ok);
            Assert.Equal(MetaFailureKind.InvalidResponse, call.Failure.Kind);
        }

        [Fact]
        public void 房间列表_实例明细为null_按无房处理不判整表非法()
        {
            MetaCall<IReadOnlyList<LobbyInstanceView>> call = Json().ParseRooms(
                "{\"rooms\":[{\"instanceId\":\"ins\",\"address\":\"h:1\",\"playerCount\":2,\"draining\":false,\"rooms\":null}]}");

            Assert.True(call.Ok);
            Assert.Single(call.Value);
            Assert.Empty(call.Value[0].Rooms);
        }

        [Fact]
        public void 房间列表_缺rooms数组_判非法()
        {
            MetaCall<IReadOnlyList<LobbyInstanceView>> call = Json().ParseRooms("{}");

            Assert.False(call.Ok);
            Assert.Equal(MetaFailureKind.InvalidResponse, call.Failure.Kind);
        }

        [Fact]
        public void 房间列表_房间行缺roomId_判非法()
        {
            MetaCall<IReadOnlyList<LobbyInstanceView>> call = Json().ParseRooms(
                "{\"rooms\":[{\"instanceId\":\"ins\",\"address\":\"h:1\",\"rooms\":[{\"phase\":1}]}]}");

            Assert.False(call.Ok);
            Assert.Equal(MetaFailureKind.InvalidResponse, call.Failure.Kind);
        }

        [Fact]
        public void 错误体_code与messageKey被取出_状态码保留()
        {
            MetaFailure failure = Json().ParseFailure(409,
                "{\"code\":\"lobby.version-mismatch\",\"messageKey\":\"meta.lobby.x\",\"args\":[]}");

            Assert.Equal(MetaFailureKind.Rejected, failure.Kind);
            Assert.Equal(409, failure.HttpStatus);
            Assert.Equal("lobby.version-mismatch", failure.Code);
            Assert.Equal("meta.lobby.x", failure.Detail);
        }

        [Fact]
        public void 错误体不可解析_code为空但状态码可分支()
        {
            MetaFailure failure = Json().ParseFailure(404, "<html>not json</html>");

            Assert.Equal(MetaFailureKind.Rejected, failure.Kind);
            Assert.Equal(404, failure.HttpStatus);
            Assert.Equal("", failure.Code);
        }

        [Fact]
        public void 请求体_由共享DTO序列化_特殊字符不破坏JSON()
        {
            var codec = new StjJsonCodec();
            MetaJson json = new MetaJson(codec);

            string body = json.BuildJoinTicketRequest("req-1", "Ro\"om", "back\\slash");

            Assert.True(codec.TryParse(body, out IJsonNode root));
            Assert.True(root.TryGetMember("requestId", out IJsonNode requestId));
            Assert.True(requestId.TryGetString(out string parsedRequestId));
            Assert.Equal("req-1", parsedRequestId);
            Assert.True(root.TryGetMember("roomId", out IJsonNode roomId));
            Assert.True(roomId.TryGetString(out string parsedRoomId));
            Assert.Equal("Ro\"om", parsedRoomId);
            Assert.True(root.TryGetMember("buildHash", out IJsonNode buildHash));
            Assert.True(buildHash.TryGetString(out string parsedBuildHash));
            Assert.Equal("back\\slash", parsedBuildHash);
        }

        [Fact]
        public void 建房请求_无模板时模板与口令为空_有模板时随请求携带()
        {
            var codec = new StjJsonCodec();
            MetaJson json = new MetaJson(codec);

            Assert.True(codec.TryParse(json.BuildCreateRoomRequest("r", "b"), out IJsonNode plain));
            Assert.True(plain.TryGetMember("mapId", out IJsonNode mapId));
            Assert.True(mapId.TryGetInt64(out long parsedMapId));
            Assert.Equal(0, parsedMapId);
            Assert.True(plain.TryGetMember("template", out IJsonNode template));
            Assert.Equal(JsonValueKind.Null, template.Kind);

            Assert.True(codec.TryParse(json.BuildCreateRoomRequest("r", "b", 3, 8, "sandbox", "k"),
                out IJsonNode withTemplate));
            Assert.True(withTemplate.TryGetMember("template", out IJsonNode templateValue));
            Assert.True(templateValue.TryGetString(out string parsedTemplate));
            Assert.Equal("sandbox", parsedTemplate);
            Assert.True(withTemplate.TryGetMember("gmKey", out IJsonNode gmKeyValue));
            Assert.True(gmKeyValue.TryGetString(out string parsedGmKey));
            Assert.Equal("k", parsedGmKey);
        }

        [Fact]
        public void 战绩查询_可选字段缺省为0_必需字段缺失判非法()
        {
            MetaCall<IReadOnlyList<MatchRecordInfo>> ok = Json().ParseMatches(
                "{\"matches\":[{\"matchId\":\"m-1\",\"kills\":3,\"deaths\":1,\"finishedAtMs\":789}]}");
            Assert.True(ok.Ok);
            Assert.Equal(0, ok.Value[0].EndReason);
            Assert.Equal(789, ok.Value[0].FinishedAtMs);

            MetaCall<IReadOnlyList<MatchRecordInfo>> bad = Json().ParseMatches(
                "{\"matches\":[{\"matchId\":\"m-1\",\"kills\":3}]}");
            Assert.False(bad.Ok);
            Assert.Equal(MetaFailureKind.InvalidResponse, bad.Failure.Kind);
        }

        [Fact]
        public void 登录请求_deviceId按UTF8字节上限裁剪且不拆坏字符()
        {
            var codec = new StjJsonCodec();
            MetaJson json = new MetaJson(codec);

            string longAscii = new string('a', MetaWireLimits.ShortIdBytes + 40);
            Assert.True(codec.TryParse(json.BuildGuestLoginRequest("req", longAscii), out IJsonNode asciiRoot));
            Assert.True(asciiRoot.TryGetMember("deviceId", out IJsonNode asciiNode));
            Assert.True(asciiNode.TryGetString(out string asciiDevice));
            Assert.Equal(MetaWireLimits.ShortIdBytes, System.Text.Encoding.UTF8.GetByteCount(asciiDevice));

            // 中文 3 字节/字：上限内能放 42 个（126B），第 43 个会越界——必须整字截断、不产生替换符。
            string chinese = new string('中', MetaWireLimits.ShortIdBytes);
            Assert.True(codec.TryParse(json.BuildGuestLoginRequest("req", chinese), out IJsonNode cnRoot));
            Assert.True(cnRoot.TryGetMember("deviceId", out IJsonNode cnNode));
            Assert.True(cnNode.TryGetString(out string cnDevice));
            Assert.True(System.Text.Encoding.UTF8.GetByteCount(cnDevice) <= MetaWireLimits.ShortIdBytes);
            Assert.False(cnDevice.Contains('\uFFFD'));
        }
    }
}
