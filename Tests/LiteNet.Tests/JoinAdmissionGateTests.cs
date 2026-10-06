using System;
using LiteNet.Diagnostics;
using LiteNet.Proto;
using RoomServer.Application;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 入站准入门（<see cref="JoinAdmissionGate"/>）判定链用例。
    ///
    /// **这批用例是本次拆分的核心收益**：准入链原先内嵌在 <c>ServerHost.HandleJoin</c> 里，
    /// 拒绝分支只能经"建房 + 连通 + 发包"间接验证（且多数拒绝路径不建房）。
    /// 抽出后可直接构造门并驱动 <see cref="JoinAdmissionGate.Evaluate"/>，
    /// 逐条锁定**链序**——链序决定了 nonce 是否被消费、房间是否被占。
    /// </summary>
    public sealed class JoinAdmissionGateTests
    {
        private const string Hash = "abcdef0123456789";

        /// <summary>宽松限流：把"准入判据"用例与"频率"用例隔离开（各自只让被测项成为否决项）。</summary>
        private static RateLimitSettings Loose(int ipEntry = 1000, int accountEntry = 1000)
            => new RateLimitSettings(
                new RateLimitSettings.Lane(1000, 1000),      // IpConnect
                new RateLimitSettings.Lane(ipEntry, 1000),   // IpEntry
                new RateLimitSettings.Lane(accountEntry, 1000), // AccountEntry
                new RateLimitSettings.Lane(1000, 1000),      // SessionPackets
                buckets: 8192, idleTtlMs: 120_000);

        private static JoinAdmissionGate Gate(
            IJoinTicketValidator tickets = null, RateLimitSettings limits = null, string audience = "aud")
            => new JoinAdmissionGate(tickets, new RateLimiter(limits ?? Loose()),
                new FixedAddress("10.0.0.1"), Hash, audience);

        private static JoinRequest ValidJoin(string roomId = "r1", string token = "tok", string buildHash = Hash)
            => new JoinRequest { RoomId = roomId, Token = token, BuildHash = buildHash };

        private static JoinPrincipal Valid(string accountId = "acct-1")
            => new JoinPrincipal { AccountId = accountId };

        // ---- 通过路径 ----

        [Fact]
        public void 准入_未装配验证器_字段合规即通过()
        {
            var d = Gate().Evaluate(ValidJoin(), connectionId: 1, nowMs: 1000);
            Assert.True(d.Admitted);
            Assert.Null(d.Principal);      // 未装配 = 无身份
            Assert.Null(d.Reason);
        }

        [Fact]
        public void 准入_装配验证器且票据有效_返回已验签身份()
        {
            var d = Gate(tickets: new FixedValidator(Valid("acct-7")))
                .Evaluate(ValidJoin(), connectionId: 1, nowMs: 1000);

            Assert.True(d.Admitted);
            Assert.NotNull(d.Principal);
            Assert.Equal("acct-7", d.Principal.AccountId);
        }

        // ---- 拒绝路径：逐条锁定分类码与诊断段落 ----

        [Fact]
        public void 拒绝_token缺失_归Admission段()
        {
            var d = Gate().Evaluate(ValidJoin(token: ""), connectionId: 1, nowMs: 1000);
            AssertRejected(d, DiagCode.JoinRejectedAdmission);
        }

        [Fact]
        public void 拒绝_字段超长_按UTF8字节判而非字符数()
        {
            // 40 个汉字 = 40 字符（< 64 上限）但 120 字节（> 64）——必须按字节拒
            string wide = new string('中', 40);
            Assert.True(wide.Length < JoinAdmissionGate.MaxRoomIdBytes);

            var d = Gate().Evaluate(ValidJoin(roomId: wide), connectionId: 1, nowMs: 1000);
            AssertRejected(d, DiagCode.JoinRejectedAdmission);
            Assert.Contains("超长", d.Reason);
        }

        [Fact]
        public void 拒绝_buildHash不符_归Build段且理由带两端对照()
        {
            var d = Gate().Evaluate(ValidJoin(buildHash: "wrong0000000000"), connectionId: 1, nowMs: 1000);

            AssertRejected(d, DiagCode.JoinRejectedBuildHash);
            Assert.Equal(DiagStage.Build, d.Stage);        // 版本红线走 Build 段（不是 Room）
            Assert.Contains("wrong0000000000", d.Reason);  // 带两端哈希对照（全链诊断注入点）
            Assert.Contains(Hash, d.Reason);
        }

        [Fact]
        public void 拒绝_票据无效_归Ticket段且分类为Malformed()
        {
            var d = Gate(tickets: new FixedValidator(null)).Evaluate(ValidJoin(), 1, 1000);

            AssertRejected(d, DiagCode.JoinRejectedTicket);
            Assert.Equal(JoinTicketRejection.Malformed, d.TicketRejection);
        }

        [Fact]
        public void 拒绝_票据给出明确分类_原样透出便于分账()
        {
            var d = Gate(tickets: new FixedValidator(JoinPrincipal.Rejected(JoinTicketRejection.Expired)))
                .Evaluate(ValidJoin(), 1, 1000);

            AssertRejected(d, DiagCode.JoinRejectedTicket);
            Assert.Equal(JoinTicketRejection.Expired, d.TicketRejection);
        }

        [Fact]
        public void 拒绝_理由不回显token原文_P0_3()
        {
            const string secret = "super-secret-token-value";
            var d = Gate(tickets: new FixedValidator(null)).Evaluate(ValidJoin(token: secret), 1, 1000);

            AssertRejected(d, DiagCode.JoinRejectedTicket);
            Assert.DoesNotContain(secret, d.Reason);   // 禁写 token/票据原文（《Meta 专项》§13.1）
        }

        // ---- 链序：决定 nonce 是否被消费、房是否被占 ----

        [Fact]
        public void 链序_buildHash不符时票据不被验签_不烧nonce()
        {
            // 验签会消费一次性 nonce。若版本红线排在验签之后，伪造 buildHash 的请求
            // 就能白白烧掉合法客户端的 nonce——版本红线必须先行。
            var validator = new FixedValidator(Valid());
            Gate(tickets: validator).Evaluate(ValidJoin(buildHash: "wrong0000000000"), 1, 1000);

            Assert.Equal(0, validator.CallCount);
        }

        [Fact]
        public void 链序_字段超长时票据不被验签_不烧nonce()
        {
            var validator = new FixedValidator(Valid());
            Gate(tickets: validator).Evaluate(ValidJoin(roomId: new string('中', 40)), 1, 1000);

            Assert.Equal(0, validator.CallCount);
        }

        [Fact]
        public void 链序_版本红线通过后才验签_正常路径确实调用了验证器()
        {
            var validator = new FixedValidator(Valid());
            Gate(tickets: validator).Evaluate(ValidJoin(), 1, 1000);

            Assert.Equal(1, validator.CallCount);
        }

        // ---- 限流维度 ----

        [Fact]
        public void 限流_IP入场超限_按Admission段拒绝()
        {
            var gate = Gate(limits: Loose(ipEntry: 2));

            for (int i = 0; i < 2; i++) Assert.True(gate.Evaluate(ValidJoin(), 1, 1000).Admitted);
            var d = gate.Evaluate(ValidJoin(), 1, 1000);

            AssertRejected(d, DiagCode.JoinRejectedAdmission);
            Assert.Contains("IP", d.Reason);
        }

        [Fact]
        public void 限流_地址探测不到_跳过IP维度不误拒()
        {
            // 探测不到地址 = 限不了看不见的地址 → 放行（端口契约：跳过而非全拒）
            var gate = new JoinAdmissionGate(null, new RateLimiter(Loose(ipEntry: 1)),
                new FixedAddress(null), Hash, "aud");

            for (int i = 0; i < 5; i++)
                Assert.True(gate.Evaluate(ValidJoin(), 1, 1000).Admitted);
        }

        [Fact]
        public void 限流_账号入场超限_在验签之后拒绝()
        {
            var validator = new FixedValidator(Valid("same"));
            var gate = Gate(tickets: validator, limits: Loose(accountEntry: 1));

            Assert.True(gate.Evaluate(ValidJoin(), 1, 1000).Admitted);
            var d = gate.Evaluate(ValidJoin(), 1, 1000);

            AssertRejected(d, DiagCode.JoinRejectedAdmission);
            Assert.Contains("账号", d.Reason);
            Assert.Equal(2, validator.CallCount);   // 两次都验了签（账号限速在验签之后）
        }

        // ---- 构造期形状与常量 ----

        [Fact]
        public void 构造_限流器与版本锚点必填()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new JoinAdmissionGate(null, null, new FixedAddress("1"), Hash, "aud"));
            Assert.Throws<ArgumentNullException>(() =>
                new JoinAdmissionGate(null, new RateLimiter(Loose()), new FixedAddress("1"), null, "aud"));
        }

        [Fact]
        public void 字段上限_与宿主公开常量同值_防两处漂移()
        {
            Assert.Equal(64, JoinAdmissionGate.MaxRoomIdBytes);
            Assert.Equal(256, JoinAdmissionGate.MaxTokenBytes);
            Assert.Equal(128, JoinAdmissionGate.MaxBuildHashBytes);
        }

        private static void AssertRejected(JoinAdmissionGate.Decision d, string expectedCode)
        {
            Assert.False(d.Admitted);
            Assert.Equal(expectedCode, d.Code);
            Assert.False(string.IsNullOrEmpty(d.Reason));
        }

        // ---- 替身 ----

        private sealed class FixedAddress : IRoomRemoteAddress
        {
            private readonly string _address;
            public FixedAddress(string address) { _address = address; }
            public string GetRemoteAddress(int connectionId) => _address;
        }

        /// <summary>票据验证替身：记录调用次数——用于断言"链序上验签是否已被执行"（nonce  consumption）。</summary>
        private sealed class FixedValidator : IJoinTicketValidator
        {
            private readonly JoinPrincipal _result;
            public int CallCount { get; private set; }

            public FixedValidator(JoinPrincipal result) { _result = result; }

            public JoinPrincipal Validate(string ticket, JoinContext context)
            {
                CallCount++;
                return _result;
            }
        }
    }
}
