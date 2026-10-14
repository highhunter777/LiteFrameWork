using LiteSim;
using Xunit;

namespace LiteSim.Tests
{
    /// <summary>地图内容摘要（<see cref="SimMapDigest"/>）：确定性、对几何敏感、规范文本稳定。
    /// 钉住的固定值 = **灰盒夹具自身**的口径锚（规范文本/算法任何变动都会红——变动须联动重烘与两端投影，勿轻易改格式）；
    /// 首图 Arena01 快照的实烘锚（== `Luban/Data/map.json` content_digest）见 <see cref="TestMapsTests"/>。</summary>
    public sealed class SimMapDigestTests
    {
        [Fact]
        public void 同图_重复计算_逐位一致()
        {
            string a = SimMapDigest.Compute(TestMaps.Greybox());
            string b = SimMapDigest.Compute(TestMaps.Greybox());
            Assert.Equal(a, b);
            Assert.Equal(16, a.Length);
            Assert.Matches("^[0-9a-f]{16}$", a);
        }

        [Fact]
        public void 灰盒夹具_摘要口径锚定()
        {
            // 灰盒夹具摘要的冻结锚（批2 起未变）；首图 Arena01 的行内值已由批3 实烘接管——见 TestMapsTests。
            Assert.Equal("5217bcaf52048108", SimMapDigest.Compute(TestMaps.Greybox()));
        }

        [Fact]
        public void 改障碍_摘要变()
        {
            SimMapData map = TestMaps.Greybox();
            string before = SimMapDigest.Compute(map);
            map.Obstacles[4].HalfX = 0.75f;
            Assert.NotEqual(before, SimMapDigest.Compute(map));
        }

        [Fact]
        public void 改出生点_摘要变()
        {
            SimMapData map = TestMaps.Greybox();
            string before = SimMapDigest.Compute(map);
            map.SpawnPoints[0] = new SimVector3(0f, 0f, 0f);
            Assert.NotEqual(before, SimMapDigest.Compute(map));
        }

        [Fact]
        public void 改表面类型_摘要变()
        {
            SimMapData map = TestMaps.Greybox();
            string before = SimMapDigest.Compute(map);
            map.Obstacles[0].Surface = SimObstacleSurface.Concrete;
            Assert.NotEqual(before, SimMapDigest.Compute(map));
        }

        [Fact]
        public void 规范文本_标签行与计数行稳定()
        {
            string text = SimMapDigest.CanonicalText(TestMaps.Greybox());
            Assert.StartsWith("map-digest-v1\n", text);
            Assert.Contains("ground_y 0\n", text);
            Assert.Contains("half_width 100\n", text);
            Assert.Contains("obstacles 5\n", text);
            Assert.Contains("spawns 16\n", text);
        }
    }
}
