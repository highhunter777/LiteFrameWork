using System.IO;
using LiteGame.Feedback;
using LiteGame.Feedback.DamageNumbers;
using LiteGame.Modules.Config;
using NUnit.Framework;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// B3 策略表消费面（表 → 投影 → 纯件）：表值投影、缺表回退、样式档覆盖。
    /// 表源变更的完整回归由 L1 表守卫 + 本用例共同钉住（这里读真实 .bytes，不造漂移夹具）。
    /// </summary>
    public sealed class BattleStrategyProfileEditModeTests
    {
        private static cfg.Tbbattlestrategy LoadTable()
        {
            string path = Path.Combine(Application.dataPath, "GameData", "Config", "Strategy",
                "tbbattlestrategy.bytes");
            Assert.IsTrue(File.Exists(path), $"缺策略表产物：{path}");
            return new cfg.Tbbattlestrategy(new Luban.ByteBuf(File.ReadAllBytes(path)));
        }

        [Test]
        public void 策略表投影_规则排行与伤害数字参数_与表值一致()
        {
            BattleStrategyProfile profile = BattleStrategyProfileFactory.Create(LoadTable());

            Assert.AreEqual(10800, profile.Rules.DurationFrames);
            Assert.AreEqual(20, profile.Rules.KillLimit);
            Assert.AreEqual(180, profile.Rules.RespawnDelayFrames);
            Assert.AreEqual(120, profile.Rules.SpawnProtectionFrames);
            Assert.AreEqual("kills_desc_entity_asc", profile.Rank.ToWireValue());
            Assert.AreEqual("玩家", profile.ResultNamePrefix);

            Assert.AreEqual(5f, profile.DamageStyle.Caused.FontSize, 1e-4f);
            Assert.AreEqual(6.5f, profile.DamageStyle.CausedCrit.FontSize, 1e-4f);
            Assert.AreEqual(1f, profile.DamageStyle.Caused.TopR, 1e-4f);
            Assert.AreEqual(0.84f, profile.DamageStyle.Caused.BottomR, 0.01f);
            Assert.AreEqual(0.87f, profile.DamageStyle.Caused.BottomG, 0.01f);
            Assert.AreEqual(0.94f, profile.DamageStyle.Caused.BottomB, 0.01f);
            Assert.AreEqual(20f, profile.DamageStyle.Band1Threshold, 1e-4f);
            Assert.AreEqual(1.05f, profile.DamageStyle.Band1Boost, 1e-4f);

            Assert.AreEqual(1.10f, profile.DamageMotion.FadeEndSeconds, 1e-4f);
            Assert.AreEqual(0.28f, profile.DamageMotion.PushDistance, 1e-4f);
            Assert.AreEqual(14f, profile.DamageMotion.AnchorFollowSpeed, 1e-4f);
        }

        [Test]
        public void 缺表_回落默认_与表值同口径()
        {
            BattleStrategyProfile fallback = BattleStrategyProfileFactory.Create(null);

            Assert.AreEqual(LiteSim.MatchRules.Default.DurationFrames, fallback.Rules.DurationFrames);
            Assert.AreEqual(5f, fallback.DamageStyle.Caused.FontSize, 1e-4f);
            Assert.AreEqual(DamageNumberMotion.FadeEndSeconds, fallback.DamageMotion.FadeEndSeconds, 1e-4f);
            Assert.AreEqual("kills_desc_entity_asc", fallback.Rank.ToWireValue());
        }

        [Test]
        public void 样式档_表驱动覆盖_解析器按档取字号与颜色()
        {
            var custom = new DamageNumberStyleProfile(
                new DamageNumberStyle(1f, 0f, 0f, 0.5f, 0f, 0f, 9f, 1f, DamageNumberTier.Caused),
                DamageNumberStyleResolver.CausedCritRed,
                DamageNumberStyleResolver.ReceivedRed,
                DamageNumberStyleResolver.ReceivedCritDeepRed,
                10f, 1.5f, 20f, 1.1f, 30f, 1.2f);
            var resolver = new DamageNumberStyleResolver(custom);

            DamageNumberStyle style = resolver.Resolve(30, HitLocalRole.Caused, crit: false);

            Assert.AreEqual(9f, style.FontSize, 1e-4f);
            Assert.AreEqual(1f, style.TopR, 1e-4f);
            Assert.AreEqual(0f, style.TopG, 1e-4f);
            Assert.AreEqual(1.2f, style.ScaleBoost, 1e-4f, "30 伤害命中第三带（≥30 → 1.2）");
        }
    }
}
