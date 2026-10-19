using System.Collections.Generic;
using LiteSim;
using Xunit;

namespace LiteSim.Tests
{
    /// <summary>开局/复活共用选点器：同 MatchId 确定性、同局互斥、全占满仍返回合法点。</summary>
    public sealed class SpawnPointSelectorTests
    {
        [Fact]
        public void 开局_同MatchId确定性_同局互斥()
        {
            var map = TestMaps.Greybox();
            uint usedA = 0u, usedB = 0u;
            var a = new int[4];
            var b = new int[4];
            for (int i = 0; i < 4; i++)
            {
                a[i] = SpawnPointSelector.Pick(map, usedA, SpawnPointSelector.InitialHash(42UL, "R-A", i));
                usedA |= 1u << a[i];
                b[i] = SpawnPointSelector.Pick(map, usedB, SpawnPointSelector.InitialHash(42UL, "R-A", i));
                usedB |= 1u << b[i];
            }

            Assert.Equal(a, b);
            Assert.Equal(4, new HashSet<int>(a).Count);
        }

        [Fact]
        public void 开局_不同MatchId改变选点hash()
        {
            Assert.NotEqual(
                SpawnPointSelector.InitialHash(42UL, "R-A", 0),
                SpawnPointSelector.InitialHash(42UL, "R-B", 0));
        }

        [Fact]
        public void 全部占满_仍返回合法点()
        {
            var map = TestMaps.Greybox();
            uint all = (1u << map.SpawnPointCount) - 1u;
            int index = SpawnPointSelector.Pick(map, all, 123u);
            Assert.InRange(index, 0, map.SpawnPointCount - 1);
        }
    }
}
