using System;
using System.Text;
using LiteTesting;
using RoomServer.Application;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// Join 票据验证接缝用例（《框架先行》§5-4"没有真实登录业务时也不能省略运行时的票据验证接口
    /// 与非法票据测试"；§6"真实网络链应经过与运行时一致的验证器"）。
    ///
    /// **覆盖矩阵**取自《服务端总设计》§16 安全测试清单"Join/Reconnect Ticket **过期、篡改、重放和密钥轮换**"
    /// 与商业门槛 §595"篡改、过期、重放 Ticket **全部拒绝**"。
    ///
    /// **纯 L1**：零 Socket/零墙钟/零文件——时钟由用例经 <see cref="JoinContext.NowMs"/> 手排，
    /// 故"过期"与"时钟回拨"可确定性构造（若验证器自读系统时钟，本组用例根本写不出来）。
    ///
    /// 签发方是隔离测试发行者 <see cref="TestTicketIssuer"/>，但它与验证器**共用同一份线格式**
    /// （<see cref="JoinTicketFormat"/>）——验的是验证器的判断，不是两份实现的巧合一致。
    /// </summary>
    [Trait(TestTrait.Category, TestCategory.Integration)]
    public sealed class JoinTicketValidatorTests
    {
        private const string RoomId = "Room-A";
        private const string BuildHash = "build-abc";
        private const string Audience = "cluster-1";

        /// <summary>
        /// 与 <see cref="TestTicketIssuer.Issue"/> 的 <c>audience</c> 缺省值**必须一致**。
        /// 受众校验是绑定项里第一道，排在房间/哈希/版本检查之前；改缺省值要同时改两边。
        /// </summary>
        private const string IssuerDefaultAudience = "test";

        private const long Now = 1_000_000;

        private static TestTicketIssuer NewIssuer(string kid = "k1") => TestTicketIssuer.Random(kid);

        private static HmacJoinTicketValidator NewValidator(TestTicketIssuer issuer, int nonceWindow = 16)
            => new HmacJoinTicketValidator(new[] { issuer.AsValidatorKey() }, nonceWindow);

        private static JoinContext Ctx(long nowMs = Now, string roomId = RoomId,
            string buildHash = BuildHash, string version = null)
            => new JoinContext(roomId, buildHash, IssuerDefaultAudience, nowMs) { Version = version };

        // ---- 放行路径（正例：证明拒绝不是"什么都拒"）----

        [Fact]
        public void 合法票据_验签通过且身份字段齐全()
        {
            var issuer = NewIssuer();
            var validator = NewValidator(issuer);
            string ticket = issuer.Issue("p42", RoomId, BuildHash, Now);

            JoinPrincipal p = validator.Validate(ticket, Ctx());

            Assert.True(p.IsValid, $"应放行，实际拒绝：{p.Rejection}");
            Assert.Equal("p42", p.PlayerId);
            Assert.Equal("p42", p.AccountId);
            Assert.Equal(RoomId, p.RoomId);
            Assert.Equal(JoinTicketRejection.None, p.Rejection);
        }

        // ---- 篡改（§16）----

        [Theory]
        [InlineData(2)]   // playerId  → 不能把票改成别人的身份
        [InlineData(4)]   // roomId    → 不能把票挪到别的房间
        [InlineData(6)]   // audience  → 不能改受众
        [InlineData(7)]   // buildHash → 不能绕过版本红线
        [InlineData(9)]   // nonce     → 不能改 nonce 重放
        [InlineData(10)]  // expMs     → 不能自行延长有效期
        public void 篡改任一字段_签名不匹配即拒绝(int partIndex)
        {
            var issuer = NewIssuer();
            var validator = NewValidator(issuer);
            string tampered = issuer.IssueThenTamper("p42", RoomId, BuildHash, Now, partIndex, "AAAA");

            JoinPrincipal p = validator.Validate(tampered, Ctx());

            Assert.False(p.IsValid, $"篡改第 {partIndex} 段后仍被放行");
            Assert.Equal(JoinTicketRejection.BadSignature, p.Rejection);
        }

        [Fact]
        public void 签名段非法编码_按签名不匹配拒绝且不抛()
        {
            var issuer = NewIssuer();
            var validator = NewValidator(issuer);
            string ticket = issuer.Issue("p42", RoomId, BuildHash, Now);
            string[] parts = ticket.Split('.');
            parts[12] = "!!!not-base64!!!";

            JoinPrincipal p = validator.Validate(string.Join(".", parts), Ctx());

            Assert.Equal(JoinTicketRejection.BadSignature, p.Rejection);
        }

        // ---- 过期（§16；只用服务端时钟）----

        [Fact]
        public void 过期票据_拒绝()
        {
            var issuer = NewIssuer();
            var validator = NewValidator(issuer);
            string ticket = issuer.Issue("p42", RoomId, BuildHash, Now, ttlMs: 1_000);

            JoinPrincipal p = validator.Validate(ticket, Ctx(Now + 1_000));   // 恰好到期 = 过期

            Assert.Equal(JoinTicketRejection.Expired, p.Rejection);
        }

        [Fact]
        public void 未到期前一刻_仍放行_边界不外扩()
        {
            var issuer = NewIssuer();
            var validator = NewValidator(issuer);
            string ticket = issuer.Issue("p42", RoomId, BuildHash, Now, ttlMs: 1_000);

            JoinPrincipal p = validator.Validate(ticket, Ctx(Now + 999));

            Assert.True(p.IsValid, $"到期前一刻应放行，实际：{p.Rejection}");
        }

        [Fact]
        public void 客户端时钟回拨_不影响判定_只用服务端时钟()
        {
            // 客户端本地时间与票据不符**不构成**拒绝依据（Meta §6.2"过期判定以服务端时钟为准"）。
            // 本用例证明验证器只读 JoinContext.NowMs：同一票据，服务端时间不同 → 结论不同。
            var issuer = NewIssuer();
            var validator = NewValidator(issuer);
            string ticket = issuer.Issue("p42", RoomId, BuildHash, Now, ttlMs: 1_000);

            Assert.True(validator.Validate(ticket, Ctx(Now)).IsValid);
            // 同一票据在服务端时间越界后即过期（与客户端"以为还很早"无关）
            var validator2 = NewValidator(issuer);
            Assert.Equal(JoinTicketRejection.Expired, validator2.Validate(ticket, Ctx(Now + 5_000)).Rejection);
        }

        [Fact]
        public void 尚未生效的票据_拒绝且与过期分类不同()
        {
            var issuer = NewIssuer();
            var validator = NewValidator(issuer);
            string ticket = issuer.Issue("p42", RoomId, BuildHash, Now, ttlMs: 10_000, notBeforeMs: Now + 5_000);

            JoinPrincipal p = validator.Validate(ticket, Ctx(Now + 1_000));

            Assert.Equal(JoinTicketRejection.NotYetValid, p.Rejection);   // 不是 Expired——分类要能区分
        }

        [Fact]
        public void 有效期非正_按格式非法拒绝()
        {
            var issuer = NewIssuer();
            var validator = NewValidator(issuer);
            // nbf == exp → 空有效期。注意必须显式给 notBeforeMs，否则缺省 0 会让 exp>nbf 成立。
            string ticket = issuer.Issue("p42", RoomId, BuildHash, Now, ttlMs: 0, notBeforeMs: Now);

            Assert.Equal(JoinTicketRejection.Malformed, validator.Validate(ticket, Ctx()).Rejection);
        }

        // ---- 重放（§16）----

        [Fact]
        public void 重放同一票据_第二次拒绝()
        {
            var issuer = NewIssuer();
            var validator = NewValidator(issuer);
            string ticket = issuer.Issue("p42", RoomId, BuildHash, Now, nonce: "fixed-nonce");

            Assert.True(validator.Validate(ticket, Ctx()).IsValid, "首次应放行");

            JoinPrincipal second = validator.Validate(ticket, Ctx());
            Assert.Equal(JoinTicketRejection.Replayed, second.Rejection);
        }

        [Fact]
        public void 重放判定在验签之后_伪造签名不占用nonce窗口()
        {
            // 顺序纪律：④时效 → ⑤绑定 → ⑥重放。若重放判定提前，攻击者可用乱签票据耗尽窗口
            // （拒绝服务）。本用例用**同一 nonce** 先打坏签名、再打真签名——后者必须成功。
            var issuer = NewIssuer();
            var validator = NewValidator(issuer);
            const string nonce = "shared-nonce";
            string good = issuer.Issue("p42", RoomId, BuildHash, Now, nonce: nonce);

            string[] parts = good.Split('.');
            parts[12] = Convert.ToBase64String(new byte[32]);      // 签名形状合法但值错
            Assert.Equal(JoinTicketRejection.BadSignature,
                validator.Validate(string.Join(".", parts), Ctx()).Rejection);
            Assert.True(validator.NonceWindowCount == 0, "验签失败的票据不应登记 nonce（窗口未被占用）");

            Assert.True(validator.Validate(good, Ctx()).IsValid, "坏签名不得占用 nonce 窗口");
        }

        [Fact]
        public void nonce窗口满_拒绝新票据而不淘汰旧条目()
        {
            // 有界（§20 完成定义第 4 条）但**不**淘汰：淘汰等于把已用过的 nonce 放出窗口 = 重开重放口子。
            var issuer = NewIssuer();
            var validator = NewValidator(issuer, nonceWindow: 2);

            Assert.True(validator.Validate(issuer.Issue("p1", RoomId, BuildHash, Now, nonce: "n1"), Ctx()).IsValid);
            Assert.True(validator.Validate(issuer.Issue("p2", RoomId, BuildHash, Now, nonce: "n2"), Ctx()).IsValid);

            string third = issuer.Issue("p3", RoomId, BuildHash, Now, nonce: "n3");
            Assert.Equal(JoinTicketRejection.Replayed, validator.Validate(third, Ctx()).Rejection);
            Assert.Equal(1, validator.RejectedWindowFull);
            Assert.Equal(2, validator.NonceWindowCount);

            // 首次票据仍然记着重放（没有被容量压力挤掉）
            Assert.Equal(JoinTicketRejection.Replayed,
                validator.Validate(issuer.Issue("p1", RoomId, BuildHash, Now, nonce: "n1"), Ctx()).Rejection);
        }

        [Fact]
        public void 过期nonce被清理_窗口容量随之释放()
        {
            var issuer = NewIssuer();
            var validator = NewValidator(issuer, nonceWindow: 2);

            Assert.True(validator.Validate(
                issuer.Issue("p1", RoomId, BuildHash, Now, ttlMs: 1_000, nonce: "n1"), Ctx()).IsValid);
            Assert.True(validator.Validate(
                issuer.Issue("p2", RoomId, BuildHash, Now, ttlMs: 60_000, nonce: "n2"), Ctx()).IsValid);

            // 时间越过 n1 的有效期后，验证时的周期清理应释放一格
            Assert.True(validator.Validate(
                issuer.Issue("p3", RoomId, BuildHash, Now + 2_000, nonce: "n3"), Ctx(Now + 2_000)).IsValid);
            Assert.Equal(1, validator.NoncesPurged);
            Assert.Equal(2, validator.NonceWindowCount);   // n2+n3：n1 已清、窗口未撑满
        }

        // ---- 密钥轮换（§16）----

        [Fact]
        public void 未知kid_拒绝()
        {
            var issuer = NewIssuer("k-unknown");
            var validator = NewValidator(NewIssuer("k1"));      // 只装 k1

            JoinPrincipal p = validator.Validate(issuer.Issue("p42", RoomId, BuildHash, Now), Ctx());

            Assert.Equal(JoinTicketRejection.UnknownKey, p.Rejection);
        }

        [Fact]
        public void 轮换期_新旧kid并存_两者都放行()
        {
            var oldIssuer = NewIssuer("k-old");
            var newIssuer = NewIssuer("k-new");
            var validator = new HmacJoinTicketValidator(
                new[] { oldIssuer.AsValidatorKey(), newIssuer.AsValidatorKey() });

            Assert.True(validator.Validate(oldIssuer.Issue("p42", RoomId, BuildHash, Now), Ctx()).IsValid);
            Assert.True(validator.Validate(newIssuer.Issue("p43", RoomId, BuildHash, Now), Ctx()).IsValid);
        }

        [Fact]
        public void 轮换后旧kid下线_旧票据即拒绝()
        {
            var oldIssuer = NewIssuer("k-old");
            var validator = NewValidator(NewIssuer("k-new"));   // 只留新 kid

            Assert.Equal(JoinTicketRejection.UnknownKey,
                validator.Validate(oldIssuer.Issue("p42", RoomId, BuildHash, Now), Ctx()).Rejection);
        }

        [Fact]
        public void 同kid不同密钥_按签名不匹配拒绝()
        {
            // kid 对但密钥不对（密钥泄换/装配错）——不能被 kid 命中即放行。
            var victim = NewIssuer("k1");
            var attacker = NewIssuer("k1");
            var validator = NewValidator(victim);

            Assert.Equal(JoinTicketRejection.BadSignature,
                validator.Validate(attacker.Issue("p42", RoomId, BuildHash, Now), Ctx()).Rejection);
        }

        // ---- 绑定项（§P0-6 六项绑定）----

        [Fact]
        public void 房号不符_拒绝()
        {
            var issuer = NewIssuer();
            var validator = NewValidator(issuer);
            string ticket = issuer.Issue("p42", "Room-B", BuildHash, Now);

            JoinPrincipal p = validator.Validate(ticket, Ctx(roomId: "Room-A"));

            Assert.Equal(JoinTicketRejection.RoomMismatch, p.Rejection);
        }

        [Fact]
        public void 构建哈希不符_拒绝()
        {
            var issuer = NewIssuer();
            var validator = NewValidator(issuer);
            string ticket = issuer.Issue("p42", RoomId, "build-OLD", Now);

            JoinPrincipal p = validator.Validate(ticket, Ctx(buildHash: "build-NEW"));

            Assert.Equal(JoinTicketRejection.BuildHashMismatch, p.Rejection);
        }

        [Fact]
        public void 受众不符_拒绝()
        {
            var issuer = NewIssuer();
            var validator = NewValidator(issuer);
            string ticket = issuer.Issue("p42", RoomId, BuildHash, Now, audience: "cluster-OTHER");

            // 只有受众不匹配，其余绑定项都对 → 必须恰好落到 AudienceMismatch
            var ctx = new JoinContext(RoomId, BuildHash, Audience, Now);
            JoinPrincipal p = validator.Validate(ticket, ctx);

            Assert.Equal(JoinTicketRejection.AudienceMismatch, p.Rejection);
        }

        [Fact]
        public void 受众未配置时_跳过受众校验()
        {
            // 空 audience = 本服不做受众校验（单集群部署形态）；其余绑定项仍然必须过。
            var issuer = NewIssuer();
            var validator = NewValidator(issuer);
            string ticket = issuer.Issue("p42", RoomId, BuildHash, Now, audience: "anything");

            var ctx = new JoinContext(RoomId, BuildHash, "", Now);
            Assert.True(validator.Validate(ticket, ctx).IsValid);
        }

        [Fact]
        public void 版本不符_拒绝()
        {
            var issuer = NewIssuer();
            var validator = NewValidator(issuer);
            string ticket = issuer.Issue("p42", RoomId, BuildHash, Now, simVersion: "2");

            JoinPrincipal p = validator.Validate(ticket, Ctx(version: "1"));

            Assert.Equal(JoinTicketRejection.VersionMismatch, p.Rejection);
        }

        // ---- 形状与边界（fail-closed，全部不得抛出）----

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("garbage")]
        [InlineData("v1.only.four.parts")]
        [InlineData("v2.k1.a.b.c.d.e.f.g.h.1.1.sig")]      // 版本不符
        public void 形状非法_按对应分类拒绝且不抛(string ticket)
        {
            var validator = NewValidator(NewIssuer());

            JoinPrincipal p = validator.Validate(ticket, Ctx());

            Assert.False(p.IsValid);
            Assert.True(p.Rejection == JoinTicketRejection.Missing || p.Rejection == JoinTicketRejection.Malformed,
                $"期望 Missing/Malformed，实际 {p.Rejection}");
        }

        [Fact]
        public void 票据超长_解析前即拒绝()
        {
            var validator = new HmacJoinTicketValidator(new[] { NewIssuer().AsValidatorKey() }, maxTokenBytes: 64);
            string huge = "v1." + new string('A', 500);

            Assert.Equal(JoinTicketRejection.Malformed, validator.Validate(huge, Ctx()).Rejection);
        }

        [Fact]
        public void 拒绝结果不带任何身份残留()
        {
            // fail-closed：拒绝路径不得留下可用于旁路的身份字段。
            var issuer = NewIssuer();
            var validator = NewValidator(issuer);
            string ticket = issuer.Issue("p42", "Room-B", BuildHash, Now);

            JoinPrincipal p = validator.Validate(ticket, Ctx(roomId: "Room-A"));

            Assert.Null(p.PlayerId);
            Assert.Null(p.AccountId);
            Assert.Null(p.Nonce);
        }

        [Fact]
        public void 验证器无密钥_装配即失败而非静默拒绝一切()
        {
            // 构造期就红，不留给运行时"看起来在跑、其实全拒"或更糟"全放行"。
            Assert.Throws<ArgumentException>(() => new HmacJoinTicketValidator(new JoinTicketKey[0]));
        }
    }
}
