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
    /// 角色移动动画首版（《动画模块专项设计》§7 首版角色后端：AnimatorControllerPlayable +
    /// Manual PlayableGraph；Driver = 视图速度 → 移动语义）。真资源（包内控制器）驱动，
    /// 缺包克隆 Ignore 跳过（灰盒视图无 Animator → 驱动不建播放器的降级由真机冒烟覆盖）。
    /// </summary>
    public sealed class CharacterLocomotionEditModeTests : UnityTestBase
    {
        private GameObject LoadPrefabOrIgnore()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CombatGirlsAnimationProfile.ViewPrefabPath);
            if (prefab == null) Assert.Ignore("CombatGirlsCharacterPack 未入库——真角色动画用例跳过");
            return prefab;
        }

        // ---- 后端契约 ----

        [Test]
        [Category(TestCategory.Contract)]
        public void 后端_有效状态提交_循环状态不自然结束()
        {
            var prefab = LoadPrefabOrIgnore();
            var go = Scope.Track(Object.Instantiate(prefab));
            var backend = new AnimatorAnimationBackend(go.GetComponentInChildren<Animator>(true));
            var player = new CharacterAnimationPlayer(backend, CombatGirlsAnimationProfile.Build());

            var ok = player.Play(new AnimationRequest(CharacterAnimationIds.Run, AnimationChannel.Locomotion));
            Assert.IsTrue(ok.Accepted, "已登记且控制器存在的状态必须被接受");
            Assert.IsTrue(backend.IsChannelActive(AnimationChannel.Locomotion));

            for (int i = 0; i < 30; i++) player.Tick(0.1f);                 // 3s：Run 为循环状态
            Assert.IsTrue(backend.IsChannelActive(AnimationChannel.Locomotion), "循环移动状态不产生 Completed（§5）");

            player.Dispose();                                               // §9 销毁序：释放后端（Graph.Destroy）
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 后端_一次性状态到边界收Completed_未知绑定收Failed()
        {
            var prefab = LoadPrefabOrIgnore();

            // ① 一次性状态（包内 Die1，非循环）→ Completed 恰好一次
            var goDie = Scope.Track(Object.Instantiate(prefab));
            var dieId = new AnimationId("Locomotion.Die");
            var dieProfile = new AnimationProfile()
                .Register(new AnimationDefinition(dieId, AnimationChannel.Locomotion, "Die1", loop: false, minSpeed: 1f, maxSpeed: 1f));
            var diePlayer = new CharacterAnimationPlayer(
                new AnimatorAnimationBackend(goDie.GetComponentInChildren<Animator>(true)), dieProfile);
            AnimationTerminalState? dieTerminal = null;
            diePlayer.OnTerminal += (h, t) => dieTerminal = t;

            Assert.IsTrue(diePlayer.Play(new AnimationRequest(dieId, AnimationChannel.Locomotion)).Accepted);
            for (int i = 0; i < 60 && dieTerminal == null; i++) diePlayer.Tick(0.1f);   // ≤6s：死亡动画时长有界
            Assert.AreEqual(AnimationTerminalState.Completed, dieTerminal, "一次性状态到边界应收 Completed");

            dieTerminal = null;
            diePlayer.Tick(1f);
            Assert.IsNull(dieTerminal, "Completed 恰好一次——之后不再重复回报");
            diePlayer.Dispose();

            // ② 未知状态绑定 → 后端显性拒绝（不静默留在旧姿态假装成功）
            var goBad = Scope.Track(Object.Instantiate(prefab));
            var badId = new AnimationId("Locomotion.NoSuch");
            var badProfile = new AnimationProfile()
                .Register(new AnimationDefinition(badId, AnimationChannel.Locomotion, "NoSuchState", loop: true, minSpeed: 1f, maxSpeed: 1f));
            var badPlayer = new CharacterAnimationPlayer(
                new AnimatorAnimationBackend(goBad.GetComponentInChildren<Animator>(true)), badProfile);
            AnimationTerminalState? badTerminal = null;
            badPlayer.OnTerminal += (h, t) => badTerminal = t;

            Assert.IsTrue(badPlayer.Play(new AnimationRequest(badId, AnimationChannel.Locomotion)).Accepted, "播放器接受（绑定由后端执行时裁决）");
            Assert.AreEqual(AnimationTerminalState.Failed, badTerminal, "控制器无该状态——后端执行失败必须回报（§3）");
            badPlayer.Dispose();
        }

        // ---- 驱动（视图速度分档 + 回收收口）----

        [Test]
        [Category(TestCategory.Contract)]
        public void 驱动_速度分档_静止回Idle_回收收口()
        {
            var prefab = LoadPrefabOrIgnore();
            var world = new SimWorldState { RngState = 1UL };
            world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f), Yaw = 0f }, out _);

            SimView view = new SimView(world, null,
                factory: (loc, parent) =>
                {
                    var go = Object.Instantiate(prefab, parent);
                    go.name = loc;
                    Scope.Track(go);
                    return go;
                },
                recycler: null);                                            // 自有池：回收 = 停用入池
            var driver = new CharacterLocomotionDriver(view);
            const float dt = 1f / 60f;

            view.Tick(dt);
            driver.Tick(dt);
            Assert.AreEqual(1, driver.AnimatedViews, "真角色视图（带 Animator）应建播放器");
            Assert.IsTrue(driver.TryGetCurrent(0, out var id) && id.Equals(CharacterAnimationIds.Idle), "初建落 Idle（不开局 T-pose）");

            // 走速档（≈1.5 m/s）→ Walk
            view.TryGetView(0, out var go2);
            for (int i = 0; i < 5; i++) { go2.transform.position += new Vector3(0.025f, 0f, 0f); driver.Tick(dt); }
            Assert.IsTrue(driver.TryGetCurrent(0, out var idWalk) && idWalk.Equals(CharacterAnimationIds.Walk), "走速档应收 Walk");

            // 全速（≈5 m/s = CombatConfig.MoveSpeed）→ Run
            for (int i = 0; i < 5; i++) { go2.transform.position += new Vector3(0.084f, 0f, 0f); driver.Tick(dt); }
            Assert.IsTrue(driver.TryGetCurrent(0, out var idRun) && idRun.Equals(CharacterAnimationIds.Run), "全速应收 Run");

            // 静止 → Idle
            for (int i = 0; i < 3; i++) driver.Tick(dt);
            Assert.IsTrue(driver.TryGetCurrent(0, out var idIdle) && idIdle.Equals(CharacterAnimationIds.Idle), "静止回 Idle");

            // 视图回收（死亡）→ 播放器随视图消失收口（Graph 不泄漏）
            world.Despawn(world.Entities[0].Id);
            view.Tick(dt);
            driver.Tick(dt);
            Assert.AreEqual(0, driver.AnimatedViews, "视图回收后播放器必须收口");

            driver.Dispose();
        }

        [Test]
        [Category(TestCategory.Unit)]
        public void 驱动_灰盒视图_无Animator不建播放器不抛()
        {
            var world = new SimWorldState { RngState = 1UL };
            world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f), Yaw = 0f }, out _);

            SimView view = new SimView(world, null,
                factory: (loc, parent) => Scope.CreateGameObject(loc),      // 灰盒形态：无 Animator
                recycler: null);
            var driver = new CharacterLocomotionDriver(view);
            const float dt = 1f / 60f;

            view.Tick(dt);
            Assert.DoesNotThrow(() => driver.Tick(dt), "无 Animator 的视图必须静默降级（表现为无动画）");
            Assert.AreEqual(0, driver.AnimatedViews);
            Assert.IsFalse(driver.TryGetCurrent(0, out _));

            driver.Dispose();
        }
    }
}
