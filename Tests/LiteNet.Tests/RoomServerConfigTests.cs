using System.IO;
using LiteTesting;
using RoomServer;
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
    /// </summary>
    [Trait(TestTrait.Category, TestCategory.Contract)]
    public sealed class RoomServerConfigTests
    {
        private const string Good = @"{
            ""port"": 17777, ""max_rooms"": 4, ""audience"": ""cluster-1"",
            ""default_template"": ""standard"",
            ""combat"": [ { ""id"":1, ""move_speed"":5, ""gravity"":-20, ""hitscan_range"":100,
                         ""hitscan_radius"":0.5, ""hitscan_height"":2, ""base_damage"":25,
                         ""damage_spread"":1, ""entity_hp"":100 } ],
            ""rooms"": { ""standard"": { ""expected_players"": 2 }, ""four"": { ""expected_players"": 4 } }
        }";

        [Fact]
        public void 合法配置_解析出端口容量与模板()
        {
            var c = RoomServerConfig.Parse(Good);

            Assert.Equal(17777, c.Port);
            Assert.Equal(4, c.MaxRooms);
            Assert.Equal("cluster-1", c.Audience);
            Assert.Contains("standard", c.TemplateIds);
            Assert.Contains("four", c.TemplateIds);
            Assert.True(c.HasTemplate(null));            // null → 默认模板
            Assert.Equal(2, c.BuildRoomConfig(null, "Any").ExpectedPlayers);
            Assert.Equal(4, c.BuildRoomConfig("four", "Any").ExpectedPlayers);
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

        // ---- 范围校验（§429）----

        [Theory]
        [InlineData(@"{""port"":0,""max_rooms"":1,""combat"": [{""id"":1}],""rooms"":{""d"":{""expected_players"":2}}}")]
        [InlineData(@"{""port"":70000,""max_rooms"":1,""combat"": [{""id"":1}],""rooms"":{""d"":{""expected_players"":2}}}")]
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
        public void 缺combat分区_拒绝()
        {
            string json = @"{
                ""port"": 17777, ""max_rooms"": 1,
                ""rooms"": { ""d"": { ""expected_players"": 2 } }
            }";
            Assert.ThrowsAny<System.Exception>(() => RoomServerConfig.Parse(json));
        }

        [Fact]
        public void 缺rooms分区_拒绝()
        {
            string json = @"{ ""port"": 17777, ""max_rooms"": 1, ""combat"": [{""id"":1}] }";
            Assert.ThrowsAny<System.Exception>(() => RoomServerConfig.Parse(json));
        }

        [Fact]
        public void rooms为空_拒绝()
        {
            string json = @"{
                ""port"": 17777, ""max_rooms"": 1, ""combat"": [{""id"":1}], ""rooms"": {}
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

        // ---- 玩法数值：进程级共享，禁止按房间覆盖 ----

        [Fact]
        public void 玩法数值为进程级_所有房间同一份()
        {
            var c = RoomServerConfig.Parse(Good);

            RoomConfig a = c.BuildRoomConfig("standard", "Room-A");
            RoomConfig b = c.BuildRoomConfig("four", "Room-B");

            // 两房间玩法数值同源（Sim 静态读），此处只能断言"配置层没有 per-room 数值"：
            // 摘要同刻由同一全局 CombatConfig 计算——见 FixedCombatConfig。
            Assert.Equal(100, c.Combat.EntityHp);
            Assert.Equal(5f, c.Combat.MoveSpeed);
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
            Assert.NotEmpty(c.TemplateIds);
            Assert.True(c.Combat.EntityHp > 0, "自带配置的玩法数值应为有效值");
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
