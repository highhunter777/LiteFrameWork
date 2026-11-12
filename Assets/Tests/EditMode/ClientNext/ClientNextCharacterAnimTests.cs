using LiteGameNext.Features.Battle.World;
using NUnit.Framework;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>角色动画态选择：优先级（死亡&gt;受击&gt;换弹&gt;瞄准&gt;移动）、四向 strafe、片段键映射。</summary>
    public sealed class ClientNextCharacterAnimTests
    {
        private static CharacterAnimFacts Facts(bool dead = false, bool aiming = false, bool reloading = false,
            float speed = 0f, Vector3 velocity = default, float hitAt = -999f, float now = 10f)
            => new CharacterAnimFacts(dead, aiming, reloading, speed, velocity, hitAt, now);

        [Test]
        public void 死亡_优先于一切()
        {
            Assert.AreEqual(CharacterAnimState.Dead,
                CharacterAnimSelector.Select(Facts(dead: true, aiming: true, reloading: true, speed: 9f)));
        }

        [Test]
        public void 受击窗口内_优先于换弹与瞄准()
        {
            Assert.AreEqual(CharacterAnimState.Hit,
                CharacterAnimSelector.Select(Facts(aiming: true, reloading: true, hitAt: 9.9f, now: 10f)));
        }

        [Test]
        public void 受击窗口外_不再播受击()
        {
            Assert.AreEqual(CharacterAnimState.AimIdle,
                CharacterAnimSelector.Select(Facts(aiming: true, hitAt: 5f, now: 10f)));
        }

        [Test]
        public void 换弹_优先于瞄准()
        {
            Assert.AreEqual(CharacterAnimState.Reload,
                CharacterAnimSelector.Select(Facts(aiming: true, reloading: true)));
        }

        [Test]
        public void 静止非瞄准_播Idle()
        {
            Assert.AreEqual(CharacterAnimState.Idle, CharacterAnimSelector.Select(Facts()));
        }

        [Test]
        public void 低速移动_播Walk_高速播Run()
        {
            Assert.AreEqual(CharacterAnimState.Walk, CharacterAnimSelector.Select(Facts(speed: 1.5f)));
            Assert.AreEqual(CharacterAnimState.Run, CharacterAnimSelector.Select(Facts(speed: 5f)));
        }

        [Test]
        public void 瞄准移动_按主轴选四向()
        {
            Assert.AreEqual(CharacterAnimState.AimWalkRight,
                CharacterAnimSelector.Select(Facts(aiming: true, speed: 2f, velocity: new Vector3(2f, 0f, 0f))));
            Assert.AreEqual(CharacterAnimState.AimWalkLeft,
                CharacterAnimSelector.Select(Facts(aiming: true, speed: 2f, velocity: new Vector3(-2f, 0f, 0f))));
            Assert.AreEqual(CharacterAnimState.AimWalkForward,
                CharacterAnimSelector.Select(Facts(aiming: true, speed: 2f, velocity: new Vector3(0f, 0f, 2f))));
            Assert.AreEqual(CharacterAnimState.AimWalkBack,
                CharacterAnimSelector.Select(Facts(aiming: true, speed: 2f, velocity: new Vector3(0f, 0f, -2f))));
        }

        [Test]
        public void 瞄准静止_播AimIdle()
        {
            Assert.AreEqual(CharacterAnimState.AimIdle, CharacterAnimSelector.Select(Facts(aiming: true)));
        }

        [Test]
        public void 片段键_与资源资产一致()
        {
            Assert.AreEqual("Idle", CharacterAnimSelector.ClipKey(CharacterAnimState.Idle));
            Assert.AreEqual("AimWalk_F", CharacterAnimSelector.ClipKey(CharacterAnimState.AimWalkForward));
            Assert.AreEqual("AimWalk_R", CharacterAnimSelector.ClipKey(CharacterAnimState.AimWalkRight));
            Assert.AreEqual("AimWalk_B", CharacterAnimSelector.ClipKey(CharacterAnimState.AimWalkBack));
            Assert.AreEqual("AimWalk_L", CharacterAnimSelector.ClipKey(CharacterAnimState.AimWalkLeft));
            Assert.AreEqual("Hit1", CharacterAnimSelector.ClipKey(CharacterAnimState.Hit));
            Assert.AreEqual("Die2", CharacterAnimSelector.ClipKey(CharacterAnimState.Dead));
        }

        [Test]
        public void 无Animator时_播放器仍可推进且不抛()
        {
            var player = new CharacterAnimPlayer(null);
            Assert.IsFalse(player.HasAnimator);
            Assert.DoesNotThrow(() => player.Apply(Facts(speed: 2f)));
            Assert.AreEqual(CharacterAnimState.Walk, player.Current);
            Assert.DoesNotThrow(() => player.Reset());
            Assert.AreEqual(CharacterAnimState.Idle, player.Current);
        }
    }
}
