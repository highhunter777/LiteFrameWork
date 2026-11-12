using System.Threading;
using Cysharp.Threading.Tasks;
using LiteGame.Modules.Config;
using LiteGameNext.Platform.Config;
using LiteSim;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>表链接消费者：tbmap 投影成真实地图（出生点/边界来自表，不是硬编码）。</summary>
    public sealed class ClientNextMapProjectionTests
    {
        private static LiteGameNext.Platform.Config.ConfigService LoadReal()
        {
            var service = new LiteGameNext.Platform.Config.ConfigService((file, ct) =>
            {
                var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>(
                    "Assets/GameData/Config/" + file + ".bytes");
                return UniTask.FromResult(asset?.bytes);
            });
            service.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
            return service;
        }

        [Test]
        public void 真实tbmap_可投影成地图()
        {
            LiteGameNext.Platform.Config.ConfigService service;
            try
            {
                service = LoadReal();
            }
            catch (LiteGameNext.Platform.Config.InvalidDataException ex)
            {
                Assert.Ignore("工程内缺表数据（需先跑 Luban/gen.bat）：" + ex.Message);
                return;
            }

            SimMapData map = MapTableProjection.Project(service.Tables);
            Assert.Greater(map.SpawnPointCount, 0, "地图必须有出生点");
            Assert.Greater(map.HalfWidth, 0f, "地图必须有宽度");
            Assert.Greater(map.HalfDepth, 0f, "地图必须有深度");
        }

        [Test]
        public void 投影结果_与硬编码平坦图不同()
        {
            LiteGameNext.Platform.Config.ConfigService service;
            try
            {
                service = LoadReal();
            }
            catch (LiteGameNext.Platform.Config.InvalidDataException ex)
            {
                Assert.Ignore("工程内缺表数据（需先跑 Luban/gen.bat）：" + ex.Message);
                return;
            }

            SimMapData fromTable = MapTableProjection.Project(service.Tables);
            // 硬编码兜底图是 20x20、2 出生点；真实表若完全相同，说明表没被真读。
            bool identical = fromTable.HalfWidth == 20f && fromTable.HalfDepth == 20f
                             && fromTable.SpawnPointCount == 2;
            Assert.IsFalse(identical, "投影结果不应与硬编码兜底图完全相同（否则表未被真读）");
        }
    }
}
