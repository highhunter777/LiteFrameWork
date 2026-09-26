using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteGame;
using LiteSim.View;
using LiteSim.View.Vfx;
using LiteTesting;
using LiteTesting.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LiteGame.Tests.UI.PlayMode
{
    /// <summary>
    /// 样例③ **单链贯通**（《框架先行》§6 样例③）：
    /// **真资源场景 → 测试角色 → 动画/特效/音效 → 卸载**，一条用例走完全程。
    ///
    /// **与分段覆盖的分工**：`SceneAndPresentationPlayModeTests` 逐段钉故障矩阵；
    /// 本组钉的是**它们能串起来**——单段绿而整链断，是分段测试看不见的失效模式。
    ///
    /// ── 素材可复现性（**如实标注，勿当成"真美术资源"**）─────────────────────────
    /// 设计原文的"测试角色/特效/音效"在本仓库的既定政策下**不是美术资产**：
    /// `Assets/{Art,Model,Animation,FX,Sound}` 与 `CombatGirlsCharacterPack` **均不入库**
    /// （`环境恢复指南` §3 第 5 条：demo 内容不入库；176MB 角色包由用户选择不入 VCS）。
    /// 故本组用**干净检出上必然存在**的素材走完整链：
    /// - **场景**：`Assets/Scenes/Test.unity`（入库）
    /// - **角色**：引擎基本体（**灰盒形态**——`GreyboxEntity` 是《表现基础》明写的对局可见性兜底件，
    ///   "能看见、能朝向"的替身；不是伪装成美术方案）
    /// - **动画**：`AnimatorAnimationBackend` 需真控制器（属角色包，不入库）→ 本组**不接动画**，
    ///   改用**特效实例的生命周期**代表"播放中的表现"
    /// - **特效**：引擎基本体经 `VfxService` 的真管线（池/表/时钟/归还）
    /// - **音效**：`AudioClip.Create` 运行时合成（不依赖 `Assets/Sound`）
    ///
    /// 这条链证明的是**串联与卸载正确**，不是"真美术资源下的表现效果"——后者需入库素材，
    /// 属 VCS 政策取舍（`环境恢复指南` §3），本组不冒充。
    /// </summary>
    [Category(TestCategory.Contract)]
    public sealed class SampleTripleChainPlayModeTests
    {
        private const string TestScene = "Assets/Scenes/Test.unity";

        private PlayModeTestScope _scope;
        private bool _assetsReady;
        private WorldClock _world;
        private UIClock _ui;
        private AudioService _audio;
        private VfxService _vfx;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _scope = new PlayModeTestScope(nameof(SampleTripleChainPlayModeTests));
            if (!_assetsReady)
            {
                var init = AssetService.InitAsync();
                yield return Wait(init, 60f);
                Assert.IsFalse(init.Status == UniTaskStatus.Pending, "资源包初始化超时（60s）");
                _assetsReady = true;
            }

            _world = new WorldClock();
            _ui = new UIClock();
            _audio = null;
            _vfx = null;

            var host = _scope.CreateGameObject("ChainClockHost");
            PlayModeTicker.Attach(host, _world.Tick, _ui.Tick);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _vfx?.Shutdown();
            _audio?.Shutdown();
            _vfx = null;
            _audio = null;
            yield return _scope.DisposeAsync();
        }

        [UnityTest]
        public IEnumerator 单链贯通_真场景到测试角色到表现到卸载_全程无残留()
        {
            // ═══ ① 真资源场景 ═══
            var scene = new SceneService();
            var load = scene.LoadSingleAsync(TestScene);
            yield return Wait(load, 30f);
            Assert.IsFalse(load.Status == UniTaskStatus.Pending, "场景加载超时");
            Assert.IsTrue(scene.IsLoaded(TestScene), "①真场景应已加载");

            // ═══ ② 测试角色（灰盒形态：引擎基本体，见类注释的素材说明）═══
            // 加载口注入灰盒产生器——**不冒充真资源**：资产目录（Assets/Model 等）本就不入库，
            // 干净检出上唯一必然可用的"角色"就是引擎基本体（GreyboxEntity 同款形态）。
            var entity = new EntityService((loc, ct) => SyncLoader(loc, ct));
            var stageRoot = _scope.CreateGameObject("ChainStage").transform;

            var show = entity.ShowAsync(GreyboxLocations.Capsule, stageRoot);
            yield return Wait(show, 30f);
            Assert.IsFalse(show.Status == UniTaskStatus.Pending, "测试角色创建超时");
            EntityHandle avatar = show.GetAwaiter().GetResult();
            Assert.IsNotNull(avatar?.GameObject, "②测试角色应就位");
            Assert.AreEqual(1, entity.ActiveCount, "②应有 1 个活动实体");
            ClientScope entityScope = avatar.Scope;
            Assert.IsNotNull(entityScope, "②实体应持有自己的作用域");
            Assert.IsFalse(entityScope.IsDisposed, "②实体作用域初始应为活");

            // ═══ ③ 表现：动画（此处不接——需入包控制器）/ 特效 / 音效 ═══
            // 特效：走 VfxService 真管线（池/表/时钟/归还），挂在测试角色下（跟随挂点）
            var vfxPool = _scope.CreateGameObject("ChainVfxPool");
            _vfx = new VfxService(SyncLoader, _world, new VfxCatalog(),
                VfxBudget.Unlimited(), stageRoot, vfxPool.transform);

            VfxHandle fx = _vfx.Play("fx_chain", avatar.GameObject.transform, follow: true);
            Assert.IsTrue(fx.IsValid, "③特效应成功提交");
            Assert.AreEqual(1, avatar.GameObject.transform.childCount,
                "③跟随挂点的特效应挂在测试角色下（挂点归属正确）");

            // 音效：运行时合成片段（不依赖 Assets/Sound）
            _audio = new AudioService();
            _audio.BindClocks(_world, _ui);
            var clip = AudioClip.Create("chain_tone", 4410, 1, 44100, false);
            int voice = _audio.Play(AudioService.Group.Effect, clip);
            Assert.AreNotEqual(0, voice, "③音效应成功播放");

            // 让真实 PlayerLoop 跑几帧：三段表现同时活着
            yield return null;
            yield return null;

            var vfxSnap = new Dictionary<string, string>();
            _vfx.Snapshot(vfxSnap);
            Assert.AreEqual("1", vfxSnap["活跃"], "③特效应仍在活跃态");

            var audioSnap = new Dictionary<string, string>();
            _audio.Snapshot(audioSnap);
            Assert.IsTrue(audioSnap.ContainsKey("Effect"), "③音效组应可观测");

            // ═══ ④ 卸载：逆序收尾，全程无残留 ═══
            // 4a. 先停表现（它们挂在角色下）
            _vfx.Stop(fx);
            _vfx.Snapshot(vfxSnap);
            Assert.AreEqual("0", vfxSnap["活跃"], "④特效应已归还");

            _audio.Stop(voice);
            _audio.Shutdown();
            yield return null;

            // 4b. 再收角色
            entity.Hide(avatar.Id);
            entity.Hide(avatar.Id);                             // **重复归还**：应被计为纪律问题但不炸
            yield return null;

            Assert.AreEqual(1, entity.DuplicateReturns,
                "④重复归还应被显式计数（可见而非静默）");
            Assert.AreEqual(0, avatar.GameObject.transform.childCount,
                "④角色回收后挂点应无残留子物体");
            // 回收时 EntityService 会 Dispose 实体作用域并**把它置 null**（HideInternal：
            // `handle.Scope?.Dispose(); handle.Scope = null;`）——故断言持有的是**当初那个实例**。
            Assert.IsTrue(entityScope.IsDisposed,
                "④实体作用域应随回收一并释放（谁创建谁取消）");

            entity.Shutdown();
            Assert.IsTrue(entity.IsShutdown);
            Assert.DoesNotThrow(() => entity.Shutdown(), "④实体服务关闭幂等");

            // 4c. 最后卸载场景
            var unload = scene.UnloadSingleAsync();
            yield return Wait(unload, 30f);
            Assert.IsFalse(scene.IsLoaded(TestScene), "④场景应已卸载");
            scene.ReleaseAll();
            Assert.IsTrue(scene.IsReleased, "④场景服务应已释放");

            // ═══ ⑤ 整链终检：无残留 ═══
            Assert.AreEqual(0, entity.ActiveCount, "⑤无活动实体残留");
            Assert.IsTrue(_vfx.IsShutdown);
            Assert.IsTrue(_audio.StatsName == "Audio");         // 关闭后仍可读（幂等不留半状态）
        }

        // ---- 辅助 ----

        /// <summary>灰盒"测试角色"的合成地址（命名即引用：VfxCatalog 同款约定，指向引擎基本体）。</summary>
        private static class GreyboxLocations
        {
            /// <summary>胶囊体——灰盒角色的既定形态（见类注释）。</summary>
            public const string Capsule = "greybox:capsule";
        }

        /// <summary>灰盒加载器：按合成地址产引擎基本体（不依赖入库模型）。
        /// **不是**在冒充真资源——它显式实现了"干净检出上必然可用"的灰盒形态。</summary>
        private static UniTask<GameObject> SyncLoader(string location, System.Threading.CancellationToken ct)
        {
            PrimitiveType shape = location.Contains("capsule") ? PrimitiveType.Capsule : PrimitiveType.Cube;
            var go = GameObject.CreatePrimitive(shape);
            go.name = "greybox:" + location;
            return UniTask.FromResult(go);
        }

        private static IEnumerator Wait(UniTask task, float timeoutSeconds)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (task.Status == UniTaskStatus.Pending && Time.realtimeSinceStartup < deadline)
                yield return null;
        }
    }
}
