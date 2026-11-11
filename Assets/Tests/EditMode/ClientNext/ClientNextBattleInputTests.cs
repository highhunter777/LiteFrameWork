using LiteGameNext.Features.Battle.Input;
using LiteSim;
using NUnit.Framework;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>输入意图→SimInputFrame：移动/开火/瞄准位集与瞄准点单源。</summary>
    public sealed class ClientNextBattleInputTests
    {
        [Test]
        public void 开火与瞄准_置对应按钮位()
        {
            var state = new BattleInputState(0.5f, -0.25f, fire: true, aim: true, new Vector3(1f, 2f, 3f));
            SimInputFrame frame = BattleInputFrame.Build(state, 42);

            Assert.AreEqual(42, frame.EntityId);
            Assert.AreEqual(0.5f, frame.MoveX);
            Assert.AreEqual(-0.25f, frame.MoveZ);
            Assert.AreEqual(1f, frame.AimPointX);
            Assert.AreEqual(2f, frame.AimPointY);
            Assert.AreEqual(3f, frame.AimPointZ);
            Assert.AreEqual(SimInputFrame.ButtonFire | SimInputFrame.ButtonAim, frame.Buttons);
        }

        [Test]
        public void 无输入_按钮位为零()
        {
            SimInputFrame frame = BattleInputFrame.Build(default, 7);
            Assert.AreEqual(0u, frame.Buttons);
            Assert.AreEqual(7, frame.EntityId);
        }
    }
}
