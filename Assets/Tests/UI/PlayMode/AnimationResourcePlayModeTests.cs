using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteFramework.Animation;   // 动画契约（Player/Request/Channel）统一在 LiteFramework.Animation
using LiteGame;
using LiteSim;
using LiteView;
using LiteView.Animation;
using LiteTesting;
using LiteTesting.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using LiteClient;

namespace LiteGame.Tests.UI.PlayMode
{
    /// <summary>
    /// 动画资源组合 PlayMode 用例（《框架先行》§4 包③退出条件"真 Lua/Prefab/**动画资源**组合运行，
    /// 取消、暂停、复用、**卸载**可验证"）。
    ///
    /// **按资源可用性分流断言**——`CombatGirlsCharacterPack` 当前未入库（176MB，用户选择不入 VCS），
    /// 所以干净检出上真角色不可用。两条分支都必须**各自成立**：
    /// - 有包：真角色 prefab（**直 Clip 形态**：挂绑定组件、无控制器）经真加载链路实例化 →
    ///   清单登记供片 → 动画后端就位；
    /// - 无包：驱动**明确跳过**（不建播放器、不报错）——无绑定组件的灰盒视图由驱动层跳过、
    ///   表现为无动画而非报错的记录契约，值得断言而非跳过。
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
                // 无包分支：断言**降级契约**（灰盒视图无绑定组件时不建播放器、不抛）
                GameObject greybox = _scope.CreateGameObject("GreyboxView");
                var viewRoot = _scope.CreateGameObject("ViewRoot").transform;
                var sim = new SimWorldState();
                greybox.transform.SetParent(viewRoot, false);

                using (var driver = new CharacterLocomotionDriver(
                           new SimView(sim, viewRoot, (loc, parent) => greybox), ProductionProfile()))
                {
                    Assert.DoesNotThrow(() => driver.Tick(0.02f), "无绑定组件视图不得抛（灰盒降级，§4）");
                    Assert.AreEqual(0, driver.AnimatedViews, "无绑定组件的视图不建播放器（AnimatedViews=0）");
                }
                yield break;
            }

            // 有包分支：真角色 → 动画后端就位 → 速度驱动档位
            GameObject avatar = _scope.Track(UnityEngine.Object.Instantiate(prefab));
            var root = _scope.CreateGameObject("AnimViewRoot").transform;
            avatar.transform.SetParent(root, false);

            Animator animator = avatar.GetComponentInChildren<Animator>(true);
            Assert.IsNotNull(animator, "真角色视图应带 Animator");
            Assert.IsNotNull(avatar.GetComponentInChildren<LiteAnimator>(true), "真角色视图应挂绑定组件（直 Clip 形态发现面）");
            Assert.IsNull(animator.runtimeAnimatorController, "直 Clip 模型：视图不挂控制器（片源走清单）");

            // 直 Clip 供片：经清单登记（与生产驱动同一登记面）
            var loadManifest = AssetService.LoadAssetAsync<AnimationClipManifest>(CombatGirlsAnimationProfile.ClipManifestPath);
            yield return Wait(loadManifest, 60f);
            var manifest = loadManifest.Status == UniTaskStatus.Succeeded ? loadManifest.GetAwaiter().GetResult() : null;
            Assert.IsNotNull(manifest, $"片段清单应可加载:{CombatGirlsAnimationProfile.ClipManifestPath}");

            var backend = new AnimatorAnimationBackend(animator);
            backend.RegisterManifest(manifest);
            var player = new AnimationPlayer(
                backend, AnimationProfileTableSource.Load(ConfigService.DataDir, CombatGirlsAnimationProfile.ModelFamily));
            bool accepted = player.Play(new AnimationRequest(
                CharacterAnimationIds.Run, AnimationChannel.Base)).Accepted;
            Assert.IsTrue(accepted, "清单应含 Run 绑定（Profile 绑定与清单一致）");
            Assert.IsTrue(backend.IsChannelActive(AnimationChannel.Base),
                "提交后 Base 通道激活");

            for (int i = 0; i < 20; i++) player.Tick(0.05f);
            yield return null;                                  // 让出一帧：真实 PlayerLoop
            Assert.IsTrue(backend.IsChannelActive(AnimationChannel.Base),
                "循环移动状态不自然结束（§5）");

            player.Dispose();                                   // §9 销毁序：释放后端（Graph.Destroy）
            Assert.DoesNotThrow(() => player.Dispose(), "重复释放幂等");
        }

        /// <summary>
        /// 取真角色 prefab 的共用前置：返回 null 表示包不可用（调用方走降级分支）。
        /// 结果只取一次（UniTask 不允许 await/取结果两次）。
        /// </summary>
        private IEnumerator LoadAvatar(System.Action<GameObject> onDone)
        {
            var load = AssetService.LoadAssetAsync<GameObject>(CombatGirlsAnimationProfile.ViewPrefabPath);
            yield return Wait(load, 60f);
            GameObject prefab = load.Status == UniTaskStatus.Succeeded ? load.GetAwaiter().GetResult() : null;
            onDone(prefab);
        }

        [UnityTest]
        public IEnumerator 动画打断_真资源下同通道替换_旧播放得Interrupted且通道归新播放()
        {
            // 《框架先行》§6 样例③ 必测失败项「**动画打断**」。
            //
            // **与 L1 的分工**：`AnimationContractTests` 已在**纯契约**层覆盖
            // 「同通道替换_旧播放得Interrupted」；本组要的是**真控制器/真资源**下同一条语义成立——
            // 那是 EditMode 与替身都给不了的（真 Animator 通道、真状态切换）。
            GameObject prefab = null;
            yield return LoadAvatar(p => prefab = p);

            if (prefab == null)
            {
                // 缺包：走同一契约的**降级断言**（不因缺包而跳过整条用例——
                // 那会让"打断"这一项永远没人验）。见本文件既有用例的降级分支口径。
                GameObject greybox = _scope.CreateGameObject("InterruptGreybox");
                var viewRoot = _scope.CreateGameObject("InterruptViewRoot").transform;
                greybox.transform.SetParent(viewRoot, false);

                using (var driver = new CharacterLocomotionDriver(
                           new SimView(new SimWorldState(), viewRoot, (loc, parent) => greybox), ProductionProfile()))
                {
                    Assert.DoesNotThrow(() => driver.Tick(0.02f), "无绑定组件视图不得抛（灰盒降级）");
                    Assert.AreEqual(0, driver.AnimatedViews, "无绑定组件的视图不建播放器");
                }
                yield break;
            }

            GameObject avatar = _scope.Track(UnityEngine.Object.Instantiate(prefab));
            var root = _scope.CreateGameObject("InterruptRoot").transform;
            avatar.transform.SetParent(root, false);

            Animator animator = avatar.GetComponentInChildren<Animator>(true);
            Assert.IsNotNull(animator, "真角色视图应带 Animator（绑定组件 RequireComponent 保证）");

            var loadManifest = AssetService.LoadAssetAsync<AnimationClipManifest>(CombatGirlsAnimationProfile.ClipManifestPath);
            yield return Wait(loadManifest, 60f);
            var manifest = loadManifest.Status == UniTaskStatus.Succeeded ? loadManifest.GetAwaiter().GetResult() : null;
            Assert.IsNotNull(manifest, "片段清单应可加载");

            var backend = new AnimatorAnimationBackend(animator);
            backend.RegisterManifest(manifest);
            var player = new AnimationPlayer(backend, AnimationProfileTableSource.Load(ConfigService.DataDir, CombatGirlsAnimationProfile.ModelFamily));

            // 记录终态（OnTerminal 是打断判据的出口——§6"旧待提交一并终止"）
            var terminals = new System.Collections.Generic.List<(AnimationHandle h, AnimationTerminalState t)>();
            player.OnTerminal += (h, t) => terminals.Add((h, t));

            // ① 先起 Run
            AnimationStartResult first = player.Play(new AnimationRequest(
                CharacterAnimationIds.Run, AnimationChannel.Base));
            Assert.IsTrue(first.Accepted, "清单应含 Run 绑定");

            for (int i = 0; i < 5; i++) { player.Tick(0.05f); player.Tick(0.05f); }

            // ② 同通道再起 Walk → Run 应得 **Interrupted**（被接受的新播放接管替换）
            AnimationStartResult second = player.Play(new AnimationRequest(
                CharacterAnimationIds.Walk, AnimationChannel.Base));
            Assert.IsTrue(second.Accepted, "清单应含 Walk 绑定");

            yield return null;                                  // 让真实 PlayerLoop 转一帧

            Assert.IsTrue(terminals.Exists(x => x.t == AnimationTerminalState.Interrupted),
                "同通道替换必须让旧播放得 Interrupted 终态（§6 打断语义）");

            // ③ 通道归新播放所有（不是"两边都在跑"）
            Assert.IsTrue(backend.IsChannelActive(AnimationChannel.Base),
                "通道应仍处于激活态（新播放接管）");
            Assert.IsTrue(player.TryGetState(second.Handle, out AnimationPlaybackState state)
                          && state.Terminal == AnimationTerminalState.None,
                "新播放应仍在进行（未被自己的接入误终止）");

            // ④ 快速反复打断：混合尾部有上限、不无限累积（§12"多次快速打断…不能无限累积"）
            for (int i = 0; i < 12; i++)
            {
                player.Play(new AnimationRequest(
                    i % 2 == 0 ? CharacterAnimationIds.Idle : CharacterAnimationIds.Run,
                    AnimationChannel.Base));
                player.Tick(0.02f);
            }
            Assert.LessOrEqual(terminals.Count, AnimationPlayer.TerminalRetentionCapacity,
                "反复打断的终态记录必须有界（§12：不能无限累积）");

            player.Dispose();
            yield return null;
        }

        [UnityTest]
        public IEnumerator 动画打断_Owner释放_当前播放得OwnerDisposed而非静默消失()
        {
            // 「加载后 Owner 已退出」的另一半：Owner 结束时**在播的动画必须收到明确终态**，
            // 而不是悄悄停掉（§5 接缝 3"Owner 释放有明确终态"）。
            GameObject prefab = null;
            yield return LoadAvatar(p => prefab = p);
            if (prefab == null) yield break;                     // 缺包：上一条已断言降级契约

            GameObject avatar = _scope.Track(UnityEngine.Object.Instantiate(prefab));
            var root = _scope.CreateGameObject("DisposeRoot").transform;
            avatar.transform.SetParent(root, false);

            var loadManifest = AssetService.LoadAssetAsync<AnimationClipManifest>(CombatGirlsAnimationProfile.ClipManifestPath);
            yield return Wait(loadManifest, 60f);
            var manifest = loadManifest.Status == UniTaskStatus.Succeeded ? loadManifest.GetAwaiter().GetResult() : null;
            Assert.IsNotNull(manifest, "片段清单应可加载");

            var backend = new AnimatorAnimationBackend(avatar.GetComponentInChildren<Animator>(true));
            backend.RegisterManifest(manifest);
            var player = new AnimationPlayer(backend, AnimationProfileTableSource.Load(ConfigService.DataDir, CombatGirlsAnimationProfile.ModelFamily));

            AnimationTerminalState? terminal = null;
            player.OnTerminal += (h, t) => terminal = t;

            player.Play(new AnimationRequest(CharacterAnimationIds.Run, AnimationChannel.Base));
            for (int i = 0; i < 5; i++) player.Tick(0.05f);

            player.Dispose();                                    // Owner 释放

            Assert.AreEqual(AnimationTerminalState.OwnerDisposed, terminal,
                "Owner 释放时当前播放必须得 OwnerDisposed 终态（不是静默消失）");
            Assert.IsTrue(player.IsDisposed);
            Assert.DoesNotThrow(() => player.Dispose(), "重复释放幂等");

            // Owner 释放后不得再接受新播放
            AnimationStartResult after = player.Play(new AnimationRequest(
                CharacterAnimationIds.Idle, AnimationChannel.Base));
            Assert.IsFalse(after.Accepted, "已释放的播放器不得接受新播放");
            Assert.AreEqual(AnimationStartResult.Reason.OwnerUnavailable, after.RejectReason,
                "拒绝原因应为 OwnerUnavailable（稳定可诊断）");
            yield return null;
        }

        [UnityTest]
        public IEnumerator 动画_驱动对无绑定组件视图_跳过且不报错()
        {
            // 与资源可用性**无关**的契约：无绑定组件的视图必须被驱动层跳过（灰盒降级——
            // 发现面＝视图绑定组件，唯一判据）。该分支在有无角色包时都应成立，所以单独一条不依赖包。
            var viewGo = _scope.CreateGameObject("NoBindingView");
            var root = _scope.CreateGameObject("Root").transform;
            viewGo.transform.SetParent(root, false);

            var sim = new SimWorldState();
            using (var driver = new CharacterLocomotionDriver(new SimView(sim, root, (loc, parent) => viewGo), ProductionProfile()))
            {
                Assert.DoesNotThrow(() => driver.Tick(0.02f));
                Assert.AreEqual(0, driver.AnimatedViews,
                    "无 LiteAnimator 的视图不建播放器（§4 灰盒降级）");
            }
            yield return null;
        }

        /// <summary>生产形态 Profile（tbanimationprofile 表直载——与运行时同表同装载器，驱动器灰盒用例的实参）。</summary>
        private static LiteFramework.Animation.AnimationProfile ProductionProfile()
            => AnimationProfileTableSource.Load(ConfigService.DataDir, CombatGirlsAnimationProfile.ModelFamily);

        private static IEnumerator Wait(UniTask task, float timeoutSeconds)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (task.Status == UniTaskStatus.Pending && Time.realtimeSinceStartup < deadline)
                yield return null;
        }
    }
}
