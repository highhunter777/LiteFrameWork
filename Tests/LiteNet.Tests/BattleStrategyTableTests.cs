using System;
using System.IO;
using System.Linq;
using System.Reflection;
using LiteGame.Modules.Config;
using LiteSim;
using RoomServer;
using RoomServer.Runtime;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// B3 策略表消费面（服务端）：规则四值随装载产出、模板默认覆盖语义、键名单源守卫。
    /// </summary>
    public sealed class BattleStrategyTableTests
    {
        private const string NoRules =
            "{\"port\":17777,\"max_rooms\":1,\"default_template\":\"standard\",\"rooms\":{\"standard\":{\"expected_players\":2}}}";

        [Fact]
        public void 策略表_规则默认值随装载产出()
        {
            ServerTableLoad load = ServerTableLoader.LoadFromRepo();

            Assert.Equal(10800, load.Rules.DurationFrames);
            Assert.Equal(20, load.Rules.KillLimit);
            Assert.Equal(180, load.Rules.RespawnDelayFrames);
            Assert.Equal(120, load.Rules.SpawnProtectionFrames);
        }

        [Fact]
        public void 模板未显式配置规则_应用表默认()
        {
            RoomServerConfig config = RoomServerConfig.Parse(NoRules, "<test>");
            var tableRules = new MatchRules(600, 7, 90, 30);

            Assert.Equal(1, config.ApplyDefaultRules(tableRules));
            RoomConfig room = config.BuildRoomConfig("standard", "R");
            Assert.Equal(tableRules.DurationFrames, room.Rules.DurationFrames);
            Assert.Equal(tableRules.KillLimit, room.Rules.KillLimit);
            Assert.Equal(tableRules.RespawnDelayFrames, room.Rules.RespawnDelayFrames);
            Assert.Equal(tableRules.SpawnProtectionFrames, room.Rules.SpawnProtectionFrames);
        }

        [Fact]
        public void 模板显式配置任一规则_不被表默认覆盖()
        {
            const string json =
                "{\"port\":17777,\"max_rooms\":1,\"default_template\":\"standard\",\"rooms\":{\"standard\":{\"expected_players\":2,\"kill_limit\":7}}}";
            RoomServerConfig config = RoomServerConfig.Parse(json, "<test>");

            Assert.Equal(0, config.ApplyDefaultRules(new MatchRules(600, 99, 90, 30)));
            RoomConfig room = config.BuildRoomConfig("standard", "R");
            Assert.Equal(7, room.Rules.KillLimit);
            Assert.Equal(MatchRules.Default.DurationFrames, room.Rules.DurationFrames);
        }

        [Fact]
        public void 键名单源_表产物逐键在册()
        {
            string path = Path.Combine(ServerTableLoader.FindRepoRoot(), ServerTableLoader.RelativeDir,
                "Strategy", "tbbattlestrategy.bytes");
            var table = new cfg.Tbbattlestrategy(new Luban.ByteBuf(File.ReadAllBytes(path)));

            FieldInfo[] keys = typeof(BattleStrategyTableReader.Keys)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string))
                .ToArray();
            Assert.NotEmpty(keys);
            foreach (FieldInfo key in keys)
            {
                string id = (string)key.GetRawConstantValue();
                Assert.NotNull(table.GetOrDefault(id));
            }
        }
    }
}
