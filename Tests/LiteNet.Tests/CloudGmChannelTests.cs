using LiteNet.Protocol;
using LiteTesting;
using RoomServer;
using RoomServer.Application;
using RoomServer.Runtime;
using Xunit;
using LiteSim.Tests;

namespace LiteNet.Tests
{
    /// <summary>
    /// 云端运维（GM）通道（《云端GM通道专项设计》§7 验收）：三件事——
    /// ① **前置拒绝**（房间/席位不存在 ⇒ 不入队）；② **队列语义**（HTTP 线程只入队，权威循环才执行）；
    /// ③ **只复用既有房间命令**（本用例不新增命令种类 ⇒ BuildHash 不变 ⇒ 客户端不需重打包）。
    /// 端点本身的 HTTP/密钥面在云上实测（本机绑端口受权限影响，不进 L1）。
    /// </summary>
    [Trait(TestTrait.Category, TestCategory.Contract)]
    public sealed class CloudGmChannelTests
    {
        private const string RoomId = "GmRoom";

        private static (ServerHost host, FakeRoomTransport t) NewHost()
        {
            var t = new FakeRoomTransport();
            var host = new ServerHost(t,
                new RoomConfig { Map = TestMaps.Greybox(), Port = 33401, RoomId = RoomId },
                map: TestMaps.Greybox());
            host.Ops.PrintEnabled = false;
            return (host, t);
        }

        private static byte[] JoinPacket(string roomId) => PacketCodec.Encode(PacketType.Join,
            new LiteNet.Proto.JoinRequest { RoomId = roomId, Token = "tok", BuildHash = ServerHost.ServerBuildHash });

        [Fact]
        public void 房间不存在_拒绝404且不入队()
        {
            var (host, _) = NewHost();
            using (host)
            {
                GmReject? reject = host.GmSubmit(GmAction.StartEarly("nope"));
                Assert.True(reject.HasValue, "房间不存在 ⇒ 拒绝");
                Assert.Equal(404, reject.Value.Status);
                Assert.Equal(0, host.GmPendingActions);
            }
        }

        [Fact]
        public void 缺room字段_拒绝400()
        {
            var (host, _) = NewHost();
            using (host)
            {
                GmReject? reject = host.GmSubmit(GmAction.Abort(null));
                Assert.True(reject.HasValue);
                Assert.Equal(400, reject.Value.Status);
            }
        }

        [Fact]
        public void 无人入座_开局拒绝409_无owner()
        {
            var (host, _) = NewHost();
            using (host)
            {
                // 内核校验"发起席位必须是 owner"——GM 不做旁路：无人入座就没人可代发
                GmReject? reject = host.GmSubmit(GmAction.StartEarly(RoomId));
                Assert.True(reject.HasValue);
                Assert.Equal(409, reject.Value.Status);
            }
        }

        [Fact]
        public void 入队后经Pump才开局_且单人也可强制开局()
        {
            var (host, t) = NewHost();
            using (host)
            {
                t.RaiseConnected(1);
                t.RaiseData(1, JoinPacket(RoomId));
                Assert.NotNull(t.LastJoinAck(1));

                Assert.False(host.GmSubmit(GmAction.StartEarly(RoomId)).HasValue, "有 owner ⇒ 入队（不拒绝）");
                Assert.Equal(1, host.GmPendingActions);            // HTTP 线程只入队——此刻尚未执行
                Assert.True(host.TryGetRoom(RoomId, out RoomRuntime before) && !before.Started,
                    "入队不等于执行（动作在权威循环内才落地）");

                host.Pump();
                Assert.True(host.TryGetRoom(RoomId, out RoomRuntime after) && after.Started,
                    "单人（1 席）也能被 GM 强制开局——ExecuteStartEarly 无人数下限");
                Assert.Equal(0, host.GmPendingActions);
            }
        }

        [Fact]
        public void 踢席位_无效席位拒绝404_有效席位经Pump断连()
        {
            var (host, t) = NewHost();
            using (host)
            {
                t.RaiseConnected(1);
                t.RaiseData(1, JoinPacket(RoomId));

                GmReject? bad = host.GmSubmit(GmAction.Kick(RoomId, 7));
                Assert.True(bad.HasValue, "不存在的席位 ⇒ 拒绝");
                Assert.Equal(404, bad.Value.Status);

                Assert.False(host.GmSubmit(GmAction.Kick(RoomId, 0)).HasValue, "已入座席位 ⇒ 入队");
                host.Pump();
                Assert.Contains(1, t.Disconnects);                  // 传输层确实被断开
            }
        }

        [Fact]
        public void 结算回灌_入队且未装配Outbox时无操作()
        {
            var (host, _) = NewHost();
            using (host)
            {
                Assert.False(host.GmSubmit(GmAction.FlushSettlement()).HasValue);
                Assert.Equal(1, host.GmPendingActions);
                host.Pump();                                        // 未装配 Outbox ⇒ 幂等无操作、不抛
                Assert.Equal(0, host.GmPendingActions);
            }
        }
    }
}
