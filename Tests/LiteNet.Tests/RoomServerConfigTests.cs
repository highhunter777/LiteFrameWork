using System.IO;
using LiteTesting;
using RoomServer;
using RoomServer.Application;
using RoomServer.Runtime;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 房间服务器配置用例（《商业级通用服务端框架总设计》§429"端口、Worker 数、**房间容量**…均有
    /// 范围校验"；§520/§600"不把估算值写死为事实""不在设计阶段虚构固定房间数"）。
    ///
    /// 本组钉住的是**配置错误必须响亮地失败**：坏文件/坏字段/越界一律抛，不静默兜底——
    /// 兜底会让配置错误变成线上的隐形分叉（与 <see cref="CombatNumbers"/> 同一口径）。
    /// 2026-09-28 起 combat 内联分区已废弃（玩法数值走 .bytes 表，见 CombatNumbersTests）。
    /// </summary>
    [Trait(TestTrait.Category, TestCategory.Contract)]
    public sealed class RoomServerConfigTests
    {
        private const string Good = @"{
            ""port"": 17777, ""max_rooms"": 4, ""audience"": ""cluster-1"",
            ""default_template"": ""standard"",
            ""rooms"": { ""standard"": { ""expected_players"": 2 }, ""four"": { ""expected_players"": 4 } }
        }";

        [Fact]
        public void 合法配置_解析出端口容量与模板()
        {
            var c = RoomServerConfig.Parse(Good);

            Assert.Equal(17777, c.Port);
            Assert.Equal(4, c.MaxRooms);
            Assert.Equal(1, c.WorkerCount);
            Assert.Equal(1024, c.MailboxCapacity);
            Assert.Equal("cluster-1", c.Audience);
            Assert.Contains("standard", c.TemplateIds);
            Assert.Contains("four", c.TemplateIds);
            Assert.True(c.HasTemplate(null));            // null → 默认模板
            Assert.Equal(2, c.BuildRoomConfig(null, "Any").ExpectedPlayers);
            Assert.Equal(4, c.BuildRoomConfig("four", "Any").ExpectedPlayers);
        }

        [Fact]
        public void 结算Outbox_缺省路径与容量可预测()
        {
            var c = RoomServerConfig.Parse(Good);

            Assert.Equal("Outbox/settlements.journal", c.SettlementJournalPath);
            Assert.Equal(10_000, c.SettlementOutboxCapacity);
            Assert.Contains("settlementJournal=Outbox/settlements.journal", c.Describe());
            Assert.Contains("settlementCapacity=10000", c.Describe());
            Assert.Contains("workerCount=1", c.Describe());
            Assert.Contains("mailboxCapacity=1024", c.Describe());
        }

        [Theory]
        [InlineData(1)]
        [InlineData(256)]
        public void Worker数量_边界值可用(int workerCount)
        {
            string json = WithProperty("worker_count", workerCount.ToString(), false);

            var c = RoomServerConfig.Parse(json);

            Assert.Equal(workerCount, c.WorkerCount);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(257)]
        public void Worker数量_越界拒绝(int workerCount)
        {
            string json = WithProperty("worker_count", workerCount.ToString(), false);

            Assert.ThrowsAny<System.Exception>(() => RoomServerConfig.Parse(json));
        }

        [Fact]
        public void Worker数量_超出Int32仍拒绝()
        {
            string json = WithProperty("worker_count", "2147483648", false);

            Assert.ThrowsAny<System.Exception>(() => RoomServerConfig.Parse(json));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(1_000_000)]
        public void WorkerMailbox容量_边界值可用(int capacity)
        {
            string json = WithProperty("mailbox_capacity", capacity.ToString(), false);

            var c = RoomServerConfig.Parse(json);

            Assert.Equal(capacity, c.MailboxCapacity);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(1_000_001)]
        public void WorkerMailbox容量_越界拒绝(int capacity)
        {
            string json = WithProperty("mailbox_capacity", capacity.ToString(), false);

            Assert.ThrowsAny<System.Exception>(() => RoomServerConfig.Parse(json));
        }

        [Fact]
        public void WorkerMailbox容量_超出Int32仍拒绝()
        {
            string json = WithProperty("mailbox_capacity", "2147483648", false);

            Assert.ThrowsAny<System.Exception>(() => RoomServerConfig.Parse(json));
        }

        [Fact]
        public void 结算日志路径_必须使用journal后缀()
        {
            string json = WithProperty("settlement_journal", "Outbox/settlements.json", true);

            Assert.ThrowsAny<System.Exception>(() => RoomServerConfig.Parse(json));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(1_000_000)]
        public void 结算Outbox容量_边界值可用(int capacity)
        {
            string json = WithProperty("settlement_outbox_capacity", capacity.ToString(), false);

            var c = RoomServerConfig.Parse(json);

            Assert.Equal(capacity, c.SettlementOutboxCapacity);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(1_000_001)]
        public void 结算Outbox容量_越界拒绝(int capacity)
        {
            string json = WithProperty("settlement_outbox_capacity", capacity.ToString(), false);

            Assert.ThrowsAny<System.Exception>(() => RoomServerConfig.Parse(json));
        }

        [Fact]
        public void 结算Outbox容量_超出Int32仍拒绝()
        {
            string json = WithProperty("settlement_outbox_capacity", "2147483648", false);

            Assert.ThrowsAny<System.Exception>(() => RoomServerConfig.Parse(json));
        }

        [Fact]
        public void 建房配置_房间号以请求为准_模板只管其余参数()
        {
            var c = RoomServerConfig.Parse(Good);

            RoomConfig cfg = c.BuildRoomConfig("four", "Room-XYZ");

            Assert.Equal("Room-XYZ", cfg.RoomId);        // 路由键来自请求
            Assert.Equal(4, cfg.ExpectedPlayers);        // 其余来自模板
            Assert.Equal(17777, cfg.Port);
        }

        // ---- 分层限流分区（R2 安全批③；§343 桶容量上限+周期清理）----

        [Fact]
        public void 限流分区_缺失走缺省()
        {
            var c = RoomServerConfig.Parse(Good);

            Assert.Equal(RateLimitSettings.Default.IpConnect.Burst, c.RateLimit.IpConnect.Burst);
            Assert.Equal(RateLimitSettings.Default.IpEntry.RefillPerSec, c.RateLimit.IpEntry.RefillPerSec);
            Assert.Equal(RateLimitSettings.Default.AccountEntry.Burst, c.RateLimit.AccountEntry.Burst);
            Assert.Equal(RateLimitSettings.Default.SessionPackets.Burst, c.RateLimit.SessionPackets.Burst);
            Assert.Equal(8192, c.RateLimit.Buckets);
            Assert.Equal(120_000, c.RateLimit.IdleTtlMs);
            Assert.Contains("rateLimit=[ip=32/4", c.Describe());
            Assert.Contains("buckets=8192", c.Describe());
        }

        [Fact]
        public void 限流分区_部分字段覆盖_其余走缺省()
        {
            string json = Good.Replace(@"""audience"": ""cluster-1"",",
                @"""audience"": ""cluster-1"",
                  ""rate_limit"": { ""session_burst"": 8, ""session_per_sec"": 3.5 }, ");

            var c = RoomServerConfig.Parse(json);

            Assert.Equal(8, c.RateLimit.SessionPackets.Burst);            // 覆盖生效
            Assert.Equal(3.5, c.RateLimit.SessionPackets.RefillPerSec);   // 小数速率可解析
            Assert.Equal(RateLimitSettings.Default.IpConnect.Burst, c.RateLimit.IpConnect.Burst);   // 其余缺省
            Assert.Equal(8192, c.RateLimit.Buckets);
        }

        [Theory]
        [InlineData("ip_burst", "0")]
        [InlineData("ip_burst", "-1")]
        [InlineData("ip_burst", "65536")]
        [InlineData("entry_per_sec", "0")]
        [InlineData("entry_per_sec", "-0.5")]
        [InlineData("account_burst", "0")]
        [InlineData("session_per_sec", "0")]
        [InlineData("buckets", "0")]
        [InlineData("buckets", "1000001")]
        [InlineData("idle_ms", "999")]
        [InlineData("idle_ms", "86400001")]
        public void 限流分区_越界拒绝(string field, string value)
        {
            string json = Good.Replace(@"""audience"": ""cluster-1"",",
                @"""audience"": ""cluster-1"",
                  ""rate_limit"": { """ + field + @""": " + value + @" }, ");

            Assert.ThrowsAny<System.Exception>(() => RoomServerConfig.Parse(json));
        }

        [Fact]
        public void 限流分区_非对象拒绝()
        {
            string json = Good.Replace(@"""audience"": ""cluster-1"",",
                @"""audience"": ""cluster-1"", ""rate_limit"": 5, ");

            Assert.ThrowsAny<System.Exception>(() => RoomServerConfig.Parse(json));
        }

        // ---- 范围校验（§429）----

        [Theory]
        [InlineData(@"{""port"":0,""max_rooms"":1,""rooms"":{""d"":{""expected_players"":2}}}")]
        [InlineData(@"{""port"":70000,""max_rooms"":1,""rooms"":{""d"":{""expected_players"":2}}}")]
        public void 端口越界_拒绝(string json)
        {
            Assert.ThrowsAny<System.Exception>(() => RoomServerConfig.Parse(json));
        }

        [Theory]
        [InlineData(0)]        // 未配置/写 0 = 无房间容量概念 → 拒（§600 不许写死）
        [InlineData(-1)]
        [InlineData(70000)]
        public void 房间容量越界_拒绝(int maxRooms)
        {
            string json = Good.Replace(@"""max_rooms"": 4", $@"""max_rooms"": {maxRooms}");
            Assert.ThrowsAny<System.Exception>(() => RoomServerConfig.Parse(json));
        }

        [Fact]
        public void 缺房间容量字段_拒绝而非取默认()
        {
            // §600"不在设计阶段虚构固定房间数"——缺字段就是没定，不能替调用方编一个。
            string json = Good.Replace(@"""max_rooms"": 4,", "");
            Assert.ThrowsAny<System.Exception>(() => RoomServerConfig.Parse(json));
        }

        [Fact]
        public void 单房间人数越界_拒绝()
        {
            string json = Good.Replace(@"{ ""expected_players"": 2 }", @"{ ""expected_players"": 0 }");
            Assert.ThrowsAny<System.Exception>(() => RoomServerConfig.Parse(json));
        }

        // ---- 结构与引用完整性 ----

        [Fact]
        public void combat分区已废弃_出现即拒绝()
        {
            // 玩法数值走 .bytes 表（CombatNumbers）；内联分区留着只会变成第二真相源——显性拒绝。
            string json = @"{
                ""port"": 17777, ""max_rooms"": 1,
                ""combat"": [{""id"":1}],
                ""rooms"": { ""d"": { ""expected_players"": 2 } }
            }";
            Assert.ThrowsAny<System.Exception>(() => RoomServerConfig.Parse(json));
        }

        [Fact]
        public void 缺rooms分区_拒绝()
        {
            string json = @"{ ""port"": 17777, ""max_rooms"": 1 }";
            Assert.ThrowsAny<System.Exception>(() => RoomServerConfig.Parse(json));
        }

        [Fact]
        public void rooms为空_拒绝()
        {
            string json = @"{
                ""port"": 17777, ""max_rooms"": 1, ""rooms"": {}
            }";
            Assert.ThrowsAny<System.Exception>(() => RoomServerConfig.Parse(json));
        }

        [Fact]
        public void 默认模板指向不存在的模板_拒绝()
        {
            string json = Good.Replace(@"""default_template"": ""standard""", @"""default_template"": ""nope""");
            Assert.ThrowsAny<System.Exception>(() => RoomServerConfig.Parse(json));
        }

        [Fact]
        public void 取不存在的模板_抛而非兜底()
        {
            // 动态建房下"默认值兜底"会让打错模板名的请求静默建成配置不对的房间。
            var c = RoomServerConfig.Parse(Good);
            Assert.ThrowsAny<System.Exception>(() => c.BuildRoomConfig("nope", "Room-A"));
        }

        [Fact]
        public void 空房间号_拒绝()
        {
            var c = RoomServerConfig.Parse(Good);
            Assert.ThrowsAny<System.Exception>(() => c.BuildRoomConfig(null, ""));
        }

        [Fact]
        public void rooms允许名为combat的模板_分区废弃后不再保留字()
        {
            // 2026-09-28：combat 内联分区废弃后，模板名空间不再需要保留字。
            const string json = @"{
                ""port"": 17777, ""max_rooms"": 1, ""default_template"": ""combat"",
                ""rooms"": { ""combat"": { ""expected_players"": 2 } }
            }";
            var c = RoomServerConfig.Parse(json);
            Assert.Contains("combat", c.TemplateIds);
            Assert.Equal(2, c.BuildRoomConfig("combat", "Room-C").ExpectedPlayers);
        }

        [Fact]
        public void 房间参数按模板隔离()
        {
            var c = RoomServerConfig.Parse(Good);

            RoomConfig a = c.BuildRoomConfig("standard", "Room-A");
            RoomConfig b = c.BuildRoomConfig("four", "Room-B");

            Assert.NotEqual(a.ExpectedPlayers, b.ExpectedPlayers);   // 房间参数可以不同
        }

        [Fact]
        public void 仓库自带配置_可解析且自洽()
        {
            // 配置文件本身也是一份交付物：语法/引用/范围都必须在门禁里被验到。
            string path = Path.Combine(RepoRoot(), RoomServerConfig.DefaultRelativePath);
            Assert.True(File.Exists(path), $"仓库配置文件缺失：{path}");

            var c = RoomServerConfig.Load(path);
            Assert.True(c.MaxRooms > 0);
            Assert.InRange(c.WorkerCount, 1, 256);
            Assert.InRange(c.MailboxCapacity, 1, 1_000_000);
            Assert.NotEmpty(c.TemplateIds);
            Assert.InRange(c.RateLimit.Buckets, 1, 1_000_000);
            Assert.True(c.RateLimit.SessionPackets.Burst >= 1);
        }

        private static string WithProperty(string property, string value, bool quoteValue)
        {
            char quote = (char)34;
            string audience = quote + "audience" + quote + ": " + quote + "cluster-1" + quote + ",";
            string rendered = quoteValue ? quote + value + quote : value;
            string addition = quote + property + quote + ": " + rendered + ",";
            return Good.Replace(audience, audience + " " + addition);
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Tests", "Tests.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir.FullName;
        }
    }
}
