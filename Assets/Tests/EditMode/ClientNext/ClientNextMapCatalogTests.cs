using LiteGameNext.Features.Battle.World;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>地图行键 → 场景路径映射：与服务端 JoinAck 定的图保持一致，未标注回退首图。</summary>
    public sealed class ClientNextMapCatalogTests
    {
        [Test]
        public void 训练场行键_映射训练场场景()
        {
            Assert.AreEqual("Assets/Scenes/TrainingGround.unity",
                BattleMapCatalog.SceneOf(BattleMapCatalog.TrainingMapId));
        }

        [Test]
        public void 首图行键_映射竞技场场景()
        {
            Assert.AreEqual("Assets/Scenes/Arena01.unity",
                BattleMapCatalog.SceneOf(BattleMapCatalog.DefaultMapId));
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void 服务端未标注_回退首图(int joinedMapId)
        {
            Assert.AreEqual(BattleMapCatalog.DefaultMapId, BattleMapCatalog.Resolve(joinedMapId));
            Assert.AreEqual("Assets/Scenes/Arena01.unity",
                BattleMapCatalog.SceneOf(BattleMapCatalog.Resolve(joinedMapId)));
        }

        [Test]
        public void 声明行键_原样采用()
        {
            Assert.AreEqual(BattleMapCatalog.TrainingMapId,
                BattleMapCatalog.Resolve(BattleMapCatalog.TrainingMapId));
        }
    }
}
