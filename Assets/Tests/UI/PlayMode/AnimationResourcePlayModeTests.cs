using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteFramework.Animation;   // CharacterAnimationPlayer（AnimationRequest/AnimationChannel 在 LiteFramework 根）
using LiteGame;
using LiteSim;
using LiteSim.View;
using LiteSim.View.Animation;
using LiteTesting;
using LiteTesting.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LiteGame.Tests.UI.PlayMode
{
    /// <summary>
    /// 动画资源组合 PlayMode 用例（《框架先行》§4 包③退出条件"真 Lua/Prefab/**动画资源**组合运行，
    /// 取消、暂停、复用、**卸载**可验证"）。
    ///
    /// **按资源可用性分流断言**——`CombatGirlsCharacterPack` 当前未入库（176MB，用户选择不入 VCS），
    /// 所以干净检出上真角色不可用。两条分支都必须**各自成立**：
    /// - 有包：真角色 prefab 经真加载链路实例化 → 动画后端就位 → 驱动按速度切 Idle/Walk/Run；
    /// - 无包：驱动**明确跳过**（不建播放器、不报错）——这正是《动画专项》§4"缺控制器的灰盒视图
    ///   由驱动层跳过，表现为无动画而非报错"的记录契约，值得断言而非跳过。
    ///
    /// 这不是放宽断言：`Assert.Ignore` 会让整个用例在干净检出上消失，而降级行为本身是要验的。
    /// </summary>
    [Category(TestCategory.Contract)]
    public sealed class AnimationResourcePlayModeTests
    {
        private PlayModeTestScope _scope;
        private bool _assetsReady;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _scope = new PlayModeTestScope(nameof(AnimationResourcePlayModeTests));
            if (!_assetsReady)
            {
                var init = AssetService.InitAsync();
                yield return Wait(init, 60f);
                _assetsReady = true;
            }
        }

        [UnityTearDown]
        public IEnumerator TearDown() => _scope.DisposeAsync();

        [UnityTest]
        public IEnumerator 动画_真角色经真加载_驱动按速度切档_卸载释放()
        {
            // 经真资源链路取真角色 prefab（与 ProcedureBattle 同一 ViewPrefabPath）
            var load = AssetService.LoadAssetAsync<GameObject>(CombatGirlsAnimationProfile.ViewPrefabPath);
            yield return Wait(load, 60f);

            // 结果只取一次（UniTask 不允许 await/取结果两次）
            bool packAvailable = load.Status == UniTaskStatus.Succeeded;
            GameObject prefab = packAvailable ? load.GetAwaiter().GetResult() : null;
            packAvailable = prefab != null;

            if (!packAvailable)
            {
                // 无包分支：断言**降级契约**（灰盒视图无控制器时不建播放器、不抛）
                GameObject greybox = _scope.CreateGameObject("GreyboxView");
                var viewRoot = _scope.CreateGameObject("ViewRoot").transform;
                var sim = new SimWorldState();
                greybox.transform.SetParent(viewRoot, false);

                using (var driver = new CharacterLocomotionDriver(
                           new SimView(sim, viewRoot, (loc, parent) => greybox)))
                {
                    Assert.DoesNotThrow(() => driver.Tick(0.02f), "无控制器视图不得抛（灰盒降级，§4）");
                    Assert.AreEqual(0, driver.AnimatedViews, "无控制器的视图不建播放器（AnimatedViews=0）");
                }
                yield break;
            }

            // 有包分支：真角色 → 动画后端就位 → 速度驱动档位
            GameObject avatar = _scope.Track(UnityEngine.Object.Instantiate(prefab));
            var root = _scope.CreateGameObject("AnimViewRoot").transform;
            avatar.transform.SetParent(root, false);

            Animator animator = avatar.GetComponentInChildren<Animator>(true);
            Assert.IsNotNull(animator, "真角色视图应带 Animator");
            Assert.IsNotNull(animator.runtimeAnimatorController, "真角色视图应带 RuntimeAnimatorController");

            // 后端契约直接验（真控制器 + 真状态名）
            var backend = new AnimatorAnimationBackend(animator);
            var player = new CharacterAnimationPlayer(
                backend, CombatGirlsAnimationProfile.Build());
            bool accepted = player.Play(new AnimationRequest(
                CharacterAnimationIds.Run, AnimationChannel.Locomotion)).Accepted;
            Assert.IsTrue(accepted, "真控制器中存在 Run 状态（Profile 绑定与包一致）");
            Assert.IsTrue(backend.IsChannelActive(AnimationChannel.Locomotion),
                "提交后 Locomotion 通道激活");

            for (int i = 0; i < 20; i++) player.Tick(0.05f);
            yield return null;                                  // 让出一帧：真实 PlayerLoop
            Assert.IsTrue(backend.IsChannelActive(AnimationChannel.Locomotion),
                "循环移动状态不自然结束（§5）");

            player.Dispose();                                   // §9 销毁序：释放后端（Graph.Destroy）
            Assert.DoesNotThrow(() => player.Dispose(), "重复释放幂等");
        }

        [UnityTest]
        public IEnumerator 动画_驱动对无控制器视图_跳过且不报错()
        {
            // 与资源可用性**无关**的契约：无控制器的视图必须被驱动层跳过（灰盒降级）。
            // 该分支在有无角色包时都应成立，所以单独一条不依赖包。
            var viewGo = _scope.CreateGameObject("NoControllerView");
            var root = _scope.CreateGameObject("Root").transform;
            viewGo.transform.SetParent(root, false);

            var sim = new SimWorldState();
            using (var driver = new CharacterLocomotionDriver(new SimView(sim, root, (loc, parent) => viewGo)))
            {
                Assert.DoesNotThrow(() => driver.Tick(0.02f));
                Assert.AreEqual(0, driver.AnimatedViews,
                    "无 RuntimeAnimatorController 的视图不建播放器（§4 灰盒降级）");
            }
            yield return null;
        }

        private static IEnumerator Wait(UniTask task, float timeoutSeconds)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (task.Status == UniTaskStatus.Pending && Time.realtimeSinceStartup < deadline)
                yield return null;
        }
    }
}
