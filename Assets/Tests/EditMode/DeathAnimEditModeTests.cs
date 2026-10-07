using System.Collections.Generic;
using LiteFramework;
using LiteFramework.Animation;
using LiteSim;
using LiteSim.View;
using LiteSim.View.Animation;
using LiteTesting;
using LiteTesting.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 死亡叶与帧锁定（die2 一次性·非循环，播完停末帧）：
    /// 真资源机器级用例（真 Profile + 真控制器 + 层次机），缺包克隆 Ignore 跳过（同移动动画先例）。
    /// 判据：①进叶播 Die2 到 Completed；②完成后恒驻 Dead 叶（不重发/不退根/不降权——句柄不变、
    /// FullBody 保持、Locomotion 不回流）；③死亡裁决压制开火（窗内死亡 → 强制迁移且不回退）。
    /// </summary>
    public sealed class DeathAnimEditModeTests : UnityTestBase
    {
        private GameObject LoadPrefabOrIgnore()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CombatGirlsAnimationProfile.ViewPrefabPath);
            if (prefab == null) Assert.Ignore("CombatGirlsCharacterPack 未入库——真角色动画用例跳过");
            return prefab;
        }

        /// <summary>真资源机器（真 Profile——Death 绑定 Die2；真控制器片段）。</summary>
        private (CharacterAnimationPlayer player, SlotAnimContext ctx,
            HierarchicalStageMachine<CharacterAnimId, CombatAnimReq> machine, AnimatorAnimationBackend backend)
            BuildMachine(GameObject go)
        {
            var backend = new AnimatorAnimationBackend(go.GetComponentInChildren<Animator>(true));
            var player = new CharacterAnimationPlayer(backend, CombatGirlsAnimationProfile.Build());
            var ctx = new SlotAnimContext
            {
                Player = player,
                MoveWeights = new float[3],
                AimWeights = new float[4],
                FireHoldSeconds = (float)CombatConfig.FireStanceFrames / SimConfig.TickRate,
            };
            var machine = CombatAnimMachine.Build(ctx);
            machine.Start(CharacterAnimId.LocomotionRoot);
            return (player, ctx, machine, backend);
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 死亡_进叶播Die2到完成_之后恒驻死叶帧锁定()
        {
            var prefab = LoadPrefabOrIgnore();
            var go = Scope.Track(Object.Instantiate(prefab));
            var (player, ctx, machine, backend) = BuildMachine(go);

            ctx.IsDead = true;
            machine.Request(CharacterAnimId.Dead);

            // 终态按**句柄**跟踪：死亡进叶会连带取消移动根的 MoveBlend 播放（通道收口的预期行为）——
            // 单变量捕获会把那个 Cancelled 误当 Die2 终态
            var terminals = new List<(AnimationHandle Handle, AnimationTerminalState State)>();
            player.OnTerminal += (h, t) => terminals.Add((h, t));
            bool deathCompleted = false;
            for (int i = 0; i < 60 && !deathCompleted; i++)
            {
                ctx.IsDead = true;
                machine.Tick(0.1f);
                player.Tick(0.1f);
                deathCompleted = terminals.Exists(t =>
                    t.Handle.Equals(ctx.BodyHandle) && t.State == AnimationTerminalState.Completed);
            }
            Assert.IsTrue(deathCompleted, "Die2（死亡句柄）一次性播完收 Completed（≤6s）");

            var handleAtEnd = ctx.BodyHandle;
            Assert.IsTrue(handleAtEnd.IsValid, "死亡句柄在完成时仍有效");

            for (int i = 0; i < 10; i++)
            {
                machine.Tick(0.1f);
                player.Tick(0.1f);
            }

            Assert.AreEqual(CharacterAnimId.Dead, machine.Current, "完成后恒驻死亡叶（不退根）");
            Assert.AreEqual(handleAtEnd, ctx.BodyHandle, "句柄不变 = 无重发（重发会按第 0 帧重播，尸体抽搐）");
            Assert.IsTrue(backend.IsChannelActive(AnimationChannel.FullBody), "FullBody 保持（姿态不丢）");
            Assert.IsFalse(backend.IsChannelActive(AnimationChannel.Locomotion), "移动根已退出不回流（Idle 不盖上来）");
            player.Dispose();
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 死亡_开火窗内到达_压制开火且不回退()
        {
            var prefab = LoadPrefabOrIgnore();
            var go = Scope.Track(Object.Instantiate(prefab));
            var (player, ctx, machine, backend) = BuildMachine(go);

            machine.Request(CharacterAnimId.FireIdle);
            machine.Tick(0.1f);
            player.Tick(0.1f);
            Assert.AreEqual(CharacterAnimId.FireIdle, machine.Current, "前置：已在开火叶");

            ctx.IsDead = true;                              // 死亡事实（窗内到达）
            machine.Tick(0.1f);
            player.Tick(0.1f);
            Assert.AreEqual(CharacterAnimId.Dead, machine.Current, "根裁决压过窗与开火叶");

            for (int i = 0; i < 20; i++)
            {
                machine.Tick(0.1f);                        // 窗尽期——退根裁决必须被死亡守卫拦住
                player.Tick(0.1f);
            }
            Assert.AreEqual(CharacterAnimId.Dead, machine.Current, "死亡后不回退（恒驻死叶）");
            player.Dispose();
        }
    }
}
