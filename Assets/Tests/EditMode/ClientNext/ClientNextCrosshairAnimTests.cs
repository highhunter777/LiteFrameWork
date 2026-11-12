using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteGameNext.Platform.Config;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>准心与动画档案投影：真实表可读、缺 id 回落、速度区间判定正确。</summary>
    public sealed class ClientNextCrosshairAnimTests
    {
        private static cfg.Tables LoadTables()
        {
            var service = new LiteGameNext.Platform.Config.ConfigService((file, ct) =>
            {
                var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>(
                    "Assets/GameData/Config/" + file + ".bytes");
                return UniTask.FromResult(asset?.bytes);
            });
            service.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
            return service.Tables;
        }

        [Test]
        public void 真实准心表_可投影()
        {
            cfg.Tables tables;
            try
            {
                tables = LoadTables();
            }
            catch (LiteGameNext.Platform.Config.InvalidDataException ex)
            {
                Assert.Ignore("工程内缺表数据（需先跑 Luban/gen.bat）：" + ex.Message);
                return;
            }

            Dictionary<int, CrosshairFeel> map = CrosshairTableProjection.Project(tables);
            Assert.Greater(map.Count, 0, "准心表应至少投影出一行");
        }

        [Test]
        public void 准心缺id_回落中性值()
        {
            var map = new Dictionary<int, CrosshairFeel>();
            CrosshairFeel feel = CrosshairTableProjection.For(map, 999);
            Assert.AreEqual(CrosshairFeel.Neutral.StaticMaxScale, feel.StaticMaxScale, 0.001f);
            Assert.AreEqual(CrosshairFeel.Neutral.BurstGrowth, feel.BurstGrowth, 0.001f);
        }

        [Test]
        public void 真实动画档案_可投影()
        {
            cfg.Tables tables;
            try
            {
                tables = LoadTables();
            }
            catch (LiteGameNext.Platform.Config.InvalidDataException ex)
            {
                Assert.Ignore("工程内缺表数据（需先跑 Luban/gen.bat）：" + ex.Message);
                return;
            }

            Dictionary<string, AnimProfile> map = AnimProfileProjection.Project(tables);
            Assert.Greater(map.Count, 0, "动画档案表应至少投影出一行");
        }

        [Test]
        public void 动画档案_速度区间判定()
        {
            var profile = new AnimProfile("Walk", "Girl", "Locomotion", "Base", "clip_walk", string.Empty,
                true, 0.1f, 3.5f, false, false, null);
            Assert.IsTrue(profile.MatchesSpeed(0.1f), "下界含");
            Assert.IsTrue(profile.MatchesSpeed(3.49f), "区间内");
            Assert.IsFalse(profile.MatchesSpeed(3.5f), "上界不含");
            Assert.IsFalse(profile.MatchesSpeed(0.05f), "低于下界");
        }

        [Test]
        public void 动画档案_无区间约束时恒匹配()
        {
            var profile = new AnimProfile("Idle", "Girl", "Locomotion", "Base", "clip_idle", string.Empty,
                true, 0f, 0f, false, false, null);
            Assert.IsTrue(profile.MatchesSpeed(0f));
            Assert.IsTrue(profile.MatchesSpeed(99f), "max<=min 视为无约束");
        }

        [Test]
        public void 按模型族过滤_只返回同族()
        {
            var map = new Dictionary<string, AnimProfile>
            {
                ["A"] = new AnimProfile("A", "Girl", "Locomotion", "Base", "c1", string.Empty,
                    true, 0f, 1f, false, false, null),
                ["B"] = new AnimProfile("B", "Robot", "Locomotion", "Base", "c2", string.Empty,
                    true, 0f, 1f, false, false, null),
            };
            List<AnimProfile> girls = AnimProfileProjection.ForFamily(map, "Girl");
            Assert.AreEqual(1, girls.Count);
            Assert.AreEqual("A", girls[0].Id);
        }
    }
}
