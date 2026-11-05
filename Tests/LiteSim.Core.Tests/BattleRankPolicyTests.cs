using LiteGame;
using Xunit;

namespace LiteSim.Tests
{
    /// <summary>
    /// 排行策略纯件（B3 表驱动消费面）：主序/并列键/名次与 Lua 事实串。
    /// 策略值来自 <c>battlestrategy</c> 表；默认必须与历史全序一致（缺表回退不改变可观行为）。
    /// </summary>
    public sealed class BattleRankPolicyTests
    {
        private static int[] Display(BattleRankPolicy policy, int[] kills, long[] ids)
        {
            var order = new int[kills.Length];
            policy.SortDisplay(kills.Length, ids, kills, order);
            return order;
        }

        [Fact]
        public void 默认_击杀降序_EntityId升序()
        {
            BattleRankPolicy policy = BattleRankPolicy.Default;
            int[] kills = { 3, 5, 5 };
            long[] ids = { 10, 20, 15 };
            Assert.Equal(new[] { 2, 1, 0 }, Display(policy, kills, ids));
            Assert.Equal("kills_desc_entity_asc", policy.ToWireValue());
        }

        [Fact]
        public void 击杀升序_少杀在前()
        {
            var policy = new BattleRankPolicy(BattleRankOrderKind.KillsAscending, BattleRankTieBreak.EntityIdAscending);
            int[] kills = { 3, 5, 1 };
            long[] ids = { 10, 20, 15 };
            Assert.Equal(new[] { 2, 0, 1 }, Display(policy, kills, ids));
        }

        [Fact]
        public void 并列键反转_同击杀按EntityId降序()
        {
            var policy = new BattleRankPolicy(BattleRankOrderKind.KillsDescending, BattleRankTieBreak.EntityIdDescending);
            int[] kills = { 5, 5, 3 };
            long[] ids = { 10, 20, 15 };
            Assert.Equal(new[] { 1, 0, 2 }, Display(policy, kills, ids));
            Assert.Equal("kills_desc_entity_desc", policy.ToWireValue());
        }

        [Fact]
        public void 名次_同击杀并列_不同击杀逐位()
        {
            BattleRankPolicy policy = BattleRankPolicy.Default;
            int[] sortedKills = { 5, 5, 3 };
            Assert.Equal(1, policy.RankAt(0, sortedKills));
            Assert.Equal(1, policy.RankAt(1, sortedKills));
            Assert.Equal(3, policy.RankAt(2, sortedKills));
        }

        [Fact]
        public void 升序事实串_保留主序与并列键两段()
        {
            var policy = new BattleRankPolicy(BattleRankOrderKind.KillsAscending, BattleRankTieBreak.EntityIdAscending);
            Assert.Equal("kills_asc_entity_asc", policy.ToWireValue());
        }
    }
}
