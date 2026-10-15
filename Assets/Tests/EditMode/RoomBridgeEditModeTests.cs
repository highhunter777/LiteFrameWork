using System;
using LiteGame;
using LiteTesting.Unity;
using NUnit.Framework;
using XLua;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// <see cref="RoomBridge"/>（Bridge.room 房间会话门面）语义守卫——真 LuaFunction 回调 + fake 会话端口
    /// （IMetaClient 先例同形——门面语义不依赖真 BattleClient；静态门面经 env.Global.Set 注入
    /// 测试全局，与生产 BindGlobals 同委托形，不走 CS 反射访问）：
    /// 回放双序（装订即回放／订阅即回放——页面先于 JoinAck 打开的竞态消解）、订阅替换语义、
    /// 无会话显性降级、被拒恰一次回调、会话摘除作废在途回调与推送。
    /// 静态门面：TearDown 必清（防跨用例污染）。
    /// </summary>
    public sealed class RoomBridgeEditModeTests : UnityTestBase
    {
        private LuaEnv _env;
        private FakeSession _face;

        [SetUp]
        public void SetUp()
        {
            _env = new LuaEnv();
            // 与生产 BindGlobals 同委托形（Func<bool>/Action<LuaFunction>——GenConfig 白名单在册形）
            _env.Global.Set("RoomBridge_OnOwner", new Action<LuaFunction>(RoomBridge.OnOwner));
            _env.Global.Set("RoomBridge_OnMembers", new Action<LuaFunction>(fn => RoomBridge.OnMembers(_env, fn)));
            _env.Global.Set("RoomBridge_StartMatch", new Action<LuaFunction>(RoomBridge.StartMatch));
            _face = new FakeSession();
        }

        [TearDown]
        public void TearDown()
        {
            RoomBridge.UnbindSession();
            RoomBridge.OnOwner(null);
            RoomBridge.OnMembers(_env, null);
            _env?.Dispose();
            _env = null;
            _face = null;
        }

        /// <summary>fake 会话端口（事实可设、事件可手动触发）。</summary>
        private sealed class FakeSession : IRoomSessionFace
        {
            public bool IsOwner { get; set; }
            public int StartCalls;
            public LiteNet.Proto.RoomSeats Seats { get; private set; }
            public event Action<LiteNet.Proto.StartMatchResponse> StartMatchDenied;
            public event Action<LiteNet.Proto.OwnerChanged> OwnerChanged;
            public event Action<LiteNet.Proto.RoomSeats> RoomSeatsChanged;

            public void SendStartMatchRequest() => StartCalls++;

            public void Deny(string reason)
                => StartMatchDenied?.Invoke(new LiteNet.Proto.StartMatchResponse { Ok = false, Reason = reason });

            public void Migrate() => OwnerChanged?.Invoke(new LiteNet.Proto.OwnerChanged { PlayerId = 1 });

            /// <summary>席位快照推进（缓存跟随 + 事件广播——模拟 RoomClient 收到全量快照）。</summary>
            public void PushSeats(params (int id, string name, bool online)[] seats)
            {
                var msg = new LiteNet.Proto.RoomSeats();
                foreach ((int id, string name, bool online) s in seats)
                    msg.Seats.Add(new LiteNet.Proto.SeatInfo { PlayerId = s.id, DisplayName = s.name, Connected = s.online });
                Seats = msg;
                RoomSeatsChanged?.Invoke(msg);
            }
        }

        /// <summary>Lua 回调登记器：owner fn（isOwner 单参）记录调用序列。</summary>
        private void BindOwnerRecorder()
            => _env.DoString(@"
owner_calls = {}
RoomBridge_OnOwner(function(isOwner) owner_calls[#owner_calls + 1] = tostring(isOwner) end)");

        private string OwnerCalls()
            => (string)_env.DoString("return table.concat(owner_calls, ',')")[0];

        private string StartCalls()
            => (string)_env.DoString("return start_calls")[0];

        [Test]
        public void 先订阅后装订_装订即回放()
        {
            BindOwnerRecorder();
            _face.IsOwner = true;                           // JoinAck 先到的序：装订时事实已在
            RoomBridge.BindSession(_face);
            Assert.AreEqual("true", OwnerCalls(), "页面已订阅：装订即回放当前事实");
        }

        [Test]
        public void 先装订后订阅_订阅即回放()
        {
            RoomBridge.BindSession(_face);
            _face.IsOwner = true;
            BindOwnerRecorder();                            // 页面晚于进房打开的序
            Assert.AreEqual("true", OwnerCalls(), "会话已在：订阅即回放");
        }

        [Test]
        public void 订阅替换语义_旧回调不再命中()
        {
            RoomBridge.BindSession(_face);
            BindOwnerRecorder();
            _env.DoString(@"
owner_calls = {}
RoomBridge_OnOwner(function(isOwner) owner_calls[#owner_calls + 1] = 'second:' .. tostring(isOwner) end)");   // 换绑
            _face.IsOwner = false;
            _face.Migrate();
            StringAssert.StartsWith("second:", OwnerCalls(), "重复登记=替换（控件族 On*Changed 同契约）");
        }

        [Test]
        public void 房主迁移推送_事实由端口重算()
        {
            RoomBridge.BindSession(_face);
            _face.IsOwner = false;
            BindOwnerRecorder();                            // 订阅即回放 false
            Assert.AreEqual("false", OwnerCalls());
            _face.IsOwner = true;                           // 端口事实变（RoomClient 已重算）
            _face.Migrate();
            Assert.AreEqual("false,true", OwnerCalls(), "迁移推送当前事实（新主到位）");
        }

        [Test]
        public void 无会话_IsOwner恒false_StartMatch显性降级()
        {
            Assert.IsFalse(RoomBridge.IsOwner, "未进房无房主事实");
            _env.DoString(@"
start_calls = ''
RoomBridge_StartMatch(function(ok, reason) start_calls = tostring(ok) .. '/' .. tostring(reason) end)");
            Assert.AreEqual("false/not-in-room", StartCalls(), "无会话点击立即回 false（迟到点击不悬挂）");
        }

        [Test]
        public void 提前开局_请求送达_被拒恰一次回调()
        {
            RoomBridge.BindSession(_face);
            _env.DoString(@"
start_calls = ''
RoomBridge_StartMatch(function(ok, reason) start_calls = start_calls .. tostring(ok) .. '/' .. tostring(reason) .. ';' end)");
            Assert.AreEqual(1, _face.StartCalls, "点击即发提前开局请求（经会话端口）");

            _face.Deny("只有房主可以开始对局");
            _face.Deny("重复包不应二次回调");
            Assert.AreEqual("false/只有房主可以开始对局;", StartCalls(),
                "被拒恰一次（成功路径不回调——OnStartGame 流程接管）");
        }

        [Test]
        public void 会话摘除_在途回调作废_推送停()
        {
            RoomBridge.BindSession(_face);
            BindOwnerRecorder();                            // 订阅即回放 false（假件默认态）
            _env.DoString(@"
start_calls = ''
RoomBridge_StartMatch(function(ok, reason) start_calls = start_calls .. 'hit' end)");
            RoomBridge.UnbindSession();                    // 离场收尾

            _face.Deny("denied");
            _face.Migrate();
            Assert.AreEqual("false", OwnerCalls(), "摘除后无新增推送（仅剩订阅时的初始回放）");
            Assert.AreEqual("", StartCalls(), "在途回调随会话作废（页面随收尾关闭，OnShow 复位标志）");
            Assert.AreEqual(1, _face.StartCalls, "摘除后无新增请求（在途一前一发为合法）");
        }

        // ---- 席位名单（OnMembers——席位实时面批） ----

        /// <summary>Lua 回调登记器：members fn（rows 行表）记录「行数[行内容]」序列。</summary>
        private void BindMembersRecorder()
            => _env.DoString(@"
member_calls = {}
RoomBridge_OnMembers(function(rows)
    local s = ''
    for i = 1, #rows do
        s = s .. rows[i].playerId .. ':' .. rows[i].name .. ':' .. tostring(rows[i].online) .. ';'
    end
    member_calls[#member_calls + 1] = tostring(#rows) .. '[' .. s .. ']'
end)");

        private string MemberCalls()
            => (string)_env.DoString("return table.concat(member_calls, '|')")[0];

        [Test]
        public void 席位名单_先订阅后装订_装订即回放()
        {
            BindMembersRecorder();
            _face.PushSeats((0, "玩家1", true), (1, "玩家2", false));   // 快照先到（订阅时无会话——不推送）
            Assert.AreEqual("", MemberCalls(), "订阅时无会话 = 不推送（显性降级，与 OnOwner 同契约）");
            RoomBridge.BindSession(_face);
            Assert.AreEqual("2[0:玩家1:true;1:玩家2:false;]", MemberCalls(), "装订即回放当前快照");
        }

        [Test]
        public void 席位名单_先装订后订阅_订阅即回放()
        {
            RoomBridge.BindSession(_face);
            _face.PushSeats((0, "玩家1", true));                        // 快照先于页面订阅到达
            BindMembersRecorder();
            Assert.AreEqual("1[0:玩家1:true;]", MemberCalls(), "订阅即回放会话当前快照");
        }

        [Test]
        public void 席位名单_事件推送_整表逐次到达()
        {
            RoomBridge.BindSession(_face);
            BindMembersRecorder();                                      // 回放：未收到过 = 空表
            Assert.AreEqual("0[]", MemberCalls());

            _face.PushSeats((0, "玩家1", true));
            _face.PushSeats((0, "玩家1", true), (1, "玩家2", true));
            _face.PushSeats((0, "玩家1", true), (1, "玩家2", false));
            Assert.AreEqual("0[]|1[0:玩家1:true;]|2[0:玩家1:true;1:玩家2:true;]|2[0:玩家1:true;1:玩家2:false;]",
                MemberCalls(), "每次广播整表到达（含在线态翻转——UI 整表重绑语义）");
        }

        [Test]
        public void 席位名单_退订与替换语义()
        {
            RoomBridge.BindSession(_face);
            BindMembersRecorder();
            RoomBridge.OnMembers(_env, null);                           // 退订
            _face.PushSeats((0, "玩家1", true));
            Assert.AreEqual("0[]", MemberCalls(), "退订后不再推送（仅剩订阅时空表回放）");

            BindMembersRecorder();                                      // 重新订阅（member_calls 重置）→ 回放当前快照
            Assert.AreEqual("1[0:玩家1:true;]", MemberCalls(), "订阅即回放（会话已在）");
        }

        [Test]
        public void 席位名单_无会话订阅_不推送不抛()
        {
            BindMembersRecorder();
            Assert.AreEqual("", MemberCalls(), "无会话 = 不推送（显性降级，与 OnOwner 同契约）");
        }
    }
}
