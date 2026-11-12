using System.Threading;
using Cysharp.Threading.Tasks;
using LiteGameNext.Platform.Config;
using LiteSim;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>战斗数值接表：判定数值与规则由表投影；表值即 C# 默认的单源证据。</summary>
    public sealed class ClientNextCombatValuesTests
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
        public void 表判定数值_可投影()
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

            CombatValues values = service.TableCombatValues;
            Assert.Greater(values.EntityHp, 0, "实体血量应来自表");
        }

        [Test]
        public void 表判定数值_与CSharp默认一致()
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

            // 这条钉住「CombatValues.Default 是表值的 C# 快照」——若表改了而默认没跟，
            // 本用例会红，从而把人工同步暴露成机制检测。
            Assert.AreEqual(CombatValues.Default, service.TableCombatValues,
                "表值应与 CombatValues.Default 逐字段一致；不一致说明表与 C# 常量已漂移");
        }

        [Test]
        public void 表规则_可读出且缺表回落默认()
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

            MatchRules rules = service.Rules(MatchRules.Default);
            Assert.Greater(rules.KillLimit, 0, "规则应来自策略表");
        }
    }
}
