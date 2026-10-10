using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using LiteTesting;
using Xunit;

namespace MetaServer.Tests
{
    /// <summary>
    /// 结算端点的**功能关闭路径**（无真实存储的宿主）：拒绝相应功能而非回退假存储——
    /// 稳定 503 <c>profile.disabled</c>（《Meta 服务专项设计》§10、《上云测试》批A）。
    ///
    /// L3（Integration trait）：真 Kestrel + 真回环 HTTP；真存储全链见 MetaServer.Integration.Tests。
    /// </summary>
    [Trait(TestTrait.Category, TestCategory.Integration)]
    public sealed class SettlementHttpTests
    {
        private static readonly byte[] InstanceKey = Filled(0x44);
        private static readonly byte[] AuthKey = Filled(0x33);
        private static readonly byte[] LobbyTicketKey = Filled(0x55);

        private static byte[] Filled(byte value)
        {
            var bytes = new byte[32];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = value;
            return bytes;
        }

        private static Task<MetaHostFixture> StartLobbyEnabledHostAsync()
        {
            return MetaHostFixture.StartAsync(map =>
            {
                map[MetaHost.ConfigSection + ":AuthSigningKeyBase64"] = Convert.ToBase64String(AuthKey);
                map[MetaHost.ConfigSection + ":LobbyInstanceKeyBase64"] = Convert.ToBase64String(InstanceKey);
                map[MetaHost.ConfigSection + ":LobbyTicketKeyBase64"] = Convert.ToBase64String(LobbyTicketKey);
            });
        }

        [Fact]
        public async Task 未配置存储_提交与查询均503功能关闭()
        {
            await using MetaHostFixture host = await StartLobbyEnabledHostAsync();
            using var http = new HttpClient();

            using var submit = new HttpRequestMessage(HttpMethod.Post, host.BaseAddress + "/matches/result");
            submit.Headers.TryAddWithoutValidation("Authorization", "Bearer " + Convert.ToBase64String(InstanceKey));
            submit.Content = new StringContent("{\"requestId\":\"r\",\"matchId\":\"m\"}", Encoding.UTF8, "application/json");
            HttpResponseMessage submitResponse = await http.SendAsync(submit);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, submitResponse.StatusCode);
            Assert.Contains("profile.disabled", await submitResponse.Content.ReadAsStringAsync());

            using var query = new HttpRequestMessage(HttpMethod.Get, host.BaseAddress + "/matches");
            query.Headers.TryAddWithoutValidation("Authorization", "Bearer " + Convert.ToBase64String(AuthKey));
            HttpResponseMessage queryResponse = await http.SendAsync(query);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, queryResponse.StatusCode);
            Assert.Contains("profile.disabled", await queryResponse.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task 未配置存储_未带凭据也是503_不先探鉴权()
        {
            // 功能门先于鉴权：功能关闭时不因凭据缺失改变语义（稳定 503），
            // 避免探测者用 401/503 差异枚举功能开关状态。
            await using MetaHostFixture host = await MetaHostFixture.StartAsync();
            using var http = new HttpClient();

            HttpResponseMessage submit = await http.PostAsync(host.BaseAddress + "/matches/result",
                new StringContent("{}", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, submit.StatusCode);
            Assert.Contains("profile.disabled", await submit.Content.ReadAsStringAsync());
        }
    }
}
