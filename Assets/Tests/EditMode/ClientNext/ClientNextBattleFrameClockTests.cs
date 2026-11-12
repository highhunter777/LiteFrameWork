using LiteGameNext.Features.Battle.World;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>本地逻辑帧时钟：最小 RollbackSim 能按 60Hz 推进空世界帧号。</summary>
    public sealed class ClientNextBattleFrameClockTests
    {
        [Test]
        public void Tick_推进逻辑帧()
        {
            var clock = new BattleFrameClock(seed: 1, expectedPlayers: 2);
            Assert.AreEqual(0, clock.Frame);
            for (int index = 0; index < 60; index++) clock.Tick(1f / 60f);
            Assert.Greater(clock.Frame, 50, "60 次 1/60 至少应推进 50 帧");
        }
    }
}
