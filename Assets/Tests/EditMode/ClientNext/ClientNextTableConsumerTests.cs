using System.Collections.Generic;
using LiteGameNext.Features.Battle.Hud;
using LiteGameNext.Features.Battle.World;
using LiteGameNext.Platform.Config;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>表参数接到消费点：动画阈值来自档案表、准心尺寸来自准心表。</summary>
    public sealed class ClientNextTableConsumerTests
    {
        private static CharacterAnimFacts Facts(float speed = 0f, bool aiming = false)
            => new CharacterAnimFacts(false, aiming, false, speed, UnityEngine.Vector3.zero, -999f, 10f);

        [Test]
        public void 无档案表_用内置阈值()
        {
            Assert.AreEqual(CharacterAnimState.Idle, CharacterAnimSelector.Select(Facts(0.05f)));
            Assert.AreEqual(CharacterAnimState.Walk, CharacterAnimSelector.Select(Facts(1.0f)));
            Assert.AreEqual(CharacterAnimState.Run, CharacterAnimSelector.Select(Facts(4.0f)));
        }

        [Test]
        public void 有档案表_按表区间选态()
        {
            // 表把 Idle 上限设为 0.5、Run 下界设为 2.0 —— 与内置 0.1/3.5 明显不同。
            var profiles = new Dictionary<string, AnimProfile>
            {
                ["Idle"] = new AnimProfile("Idle", "Girl", "Locomotion", "Base", "c", string.Empty,
                    true, 0f, 0.5f, false, false, null),
                ["Run"] = new AnimProfile("Run", "Girl", "Locomotion", "Base", "c", string.Empty,
                    true, 2.0f, 10f, false, false, null),
            };
            Assert.AreEqual(CharacterAnimState.Idle, CharacterAnimSelector.Select(Facts(0.4f), profiles),
                "0.4 < 表 Idle 上限 0.5，应为 Idle（内置阈值下会判 Walk）");
            Assert.AreEqual(CharacterAnimState.Run, CharacterAnimSelector.Select(Facts(2.5f), profiles),
                "2.5 > 表 Run 下界 2.0，应为 Run（内置阈值下会判 Walk）");
        }

        [Test]
        public void 档案表缺键_回落内置阈值()
        {
            var profiles = new Dictionary<string, AnimProfile>
            {
                ["Walk"] = new AnimProfile("Walk", "Girl", "Locomotion", "Base", "c", string.Empty,
                    true, 0.1f, 3.5f, false, false, null),
            };
            Assert.AreEqual(CharacterAnimState.Run, CharacterAnimSelector.Select(Facts(4.0f), profiles),
                "缺 Idle/Run 键时应回落内置阈值");
        }

        [Test]
        public void 准心尺寸_由表参数驱动()
        {
            var hud = new BattleHudModel();
            hud.SetAim(false, 0f);
            // 中性值：静态 1.0、零张开 → 尺寸等于基准。
            Assert.AreEqual(36f, hud.CrosshairSize(36f), 0.01f);

            hud.SetCrosshairFeel(new CrosshairFeel(1, staticMaxScale: 2f, burstGrowth: 4f,
                burstCap: 3f, kick: 0f));
            hud.SetAim(false, 0f);
            Assert.AreEqual(72f, hud.CrosshairSize(36f), 0.01f, "静态满张 2.0 → 72");

            hud.SetAim(false, 5f);
            // 36×2 + 5×4 = 92，但封顶 36×3 = 108 未触发 → 92。
            Assert.AreEqual(92f, hud.CrosshairSize(36f), 0.01f);
        }

        [Test]
        public void 准心尺寸_受上限封顶()
        {
            var hud = new BattleHudModel();
            hud.SetCrosshairFeel(new CrosshairFeel(1, staticMaxScale: 1f, burstGrowth: 100f,
                burstCap: 2f, kick: 0f));
            hud.SetAim(false, 50f);
            Assert.AreEqual(72f, hud.CrosshairSize(36f), 0.01f, "封顶 = 36×2");
        }
    }
}
