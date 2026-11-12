using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteGame.Modules.Config;
using LiteGameNext.Platform.Config;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>配置表服务：表清单单源、缺字节即抛、真实表可装载。</summary>
    public sealed class ClientNextConfigTests
    {
        [Test]
        public void 表清单_来自共享单源()
        {
            Assert.AreEqual(TableLayout.Files.Length, LiteGameNext.Platform.Config.ConfigService.TableFiles.Count);
            Assert.AreEqual(TableLayout.DataDir, LiteGameNext.Platform.Config.ConfigService.DataDir);
            CollectionAssert.Contains(LiteGameNext.Platform.Config.ConfigService.TableFiles, "Map/tbmap");
            CollectionAssert.Contains(LiteGameNext.Platform.Config.ConfigService.TableFiles, "Combat/tbweapon");
        }

        [Test]
        public void 缺字节_装载即抛()
        {
            var service = new LiteGameNext.Platform.Config.ConfigService(
                (file, ct) => UniTask.FromResult<byte[]>(null));
            // UniTask 的同步等待直接抛原始异常（不包 AggregateException）。
            Assert.Throws<InvalidDataException>(() =>
                service.LoadAsync(CancellationToken.None).GetAwaiter().GetResult());
        }

        [Test]
        public void 未装载_查表即抛()
        {
            var service = new LiteGameNext.Platform.Config.ConfigService((file, ct) => UniTask.FromResult(new byte[] { 1 }));
            Assert.IsFalse(service.Loaded);
            Assert.Throws<System.InvalidOperationException>(() => { var _ = service.Tables; });
        }

        [Test]
        public void 真实表_可从工程装载()
        {
            var service = new LiteGameNext.Platform.Config.ConfigService((file, ct) =>
            {
                string path = "Assets/GameData/Config/" + file + ".bytes";
                var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>(path);
                return UniTask.FromResult(asset?.bytes);
            });
            try
            {
                service.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (InvalidDataException ex)
            {
                Assert.Ignore("工程内缺表数据（需先跑 Luban/gen.bat）：" + ex.Message);
            }
            Assert.IsTrue(service.Loaded, "真实表应装载成功");
            Assert.AreEqual(TableLayout.Files.Length, service.LoadedTableCount);
            Assert.IsNotNull(service.Tables.Tbmap, "地图表应可读");
        }
    }
}
