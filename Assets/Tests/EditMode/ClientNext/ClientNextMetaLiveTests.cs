using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using LiteGameNext.Platform.Meta;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>真实 MetaServer 链路（本机 127.0.0.1:5000）：游客登录 → 房间列表 → Join 票据。</summary>
    public sealed class ClientNextMetaLiveTests
    {
        private const string BaseUrl = "http://127.0.0.1:5000";

        private static MetaClient Client() => new MetaClient(new UnityMetaTransport(BaseUrl, 10));

        [Test]
        [Explicit("需本机 MetaServer(127.0.0.1:5000) + MongoDB；S1-C 验收用，不进默认门禁")]
        public async Task 真实Meta_游客登录_房间列表_换票()
        {
            MetaClient client = Client();

            MetaCallResult<MetaWire.GuestLoginInfo> login = await client.GuestLoginAsync("s1c-live-device");
            Assert.IsTrue(login.Success, "游客登录失败：" + login.Error);
            Assert.IsNotEmpty(login.Value.AccessToken, "令牌不得为空");

            string token = login.Value.AccessToken;
            MetaCallResult<System.Collections.Generic.IReadOnlyList<MetaWire.LobbyInstanceView>> rooms =
                await client.ListRoomsAsync(token);
            Assert.IsTrue(rooms.Success, "房间列表失败：" + rooms.Error);
            Assert.IsNotNull(rooms.Value, "房间列表不得为 null");

            MetaCallResult<MetaWire.JoinTicketInfo> ticket =
                await client.RequestJoinTicketAsync(token, "lobby-default", LiteNet.BuildHash.Value);
            Assert.IsTrue(ticket.Success, "换票失败：" + ticket.Error);
            Assert.IsNotEmpty(ticket.Value.Ticket, "票据不得为空");
            Assert.IsNotEmpty(ticket.Value.Address, "房间地址不得为空");
            Assert.AreEqual("lobby-default", ticket.Value.RoomId, "换票应落在请求的房间");
        }
    }
}
