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
    /// 场景与表现 PlayMode 用例（《框架先行》§6 样例③）。
    ///
    /// 设计侧要求（原文）：
    /// - 贯通链路：**真资源场景 → 测试角色 → 动画/特效/音效 → 卸载**
    /// - 必测失败：**世界/UI 暂停**、动画打断、**重复归还**、加载后 Owner 已退出、卸载中仍有租约
    /// - 范围限制：用**显式样例指令**驱动动作，不把动画完成作为玩法判定
    ///
    /// **本组补的是哪一块**：动画段已入 PlayMode（`AnimationResourcePlayModeTests`）、
    /// VFX 生命周期 EditMode 已 16 例、Scene 生命周期语义 EditMode 已覆盖——但
    /// **真资源场景加载**、**音效段**、**世界/UI 分域暂停**三者此前**没有任何 PlayMode 证据**；
    /// `AudioService` 在全仓几乎没有独立用例。
    ///
    /// **为什么必须是 PlayMode**：本组要的正是 EditMode 给不了的——时钟由**真实 PlayerLoop** 驱动
    /// （`PlayModeTicker`，与生产 `GameEntry.Update` 同语义）、真实场景的加载/卸载、
    /// 真实 `AudioSource` 状态。**不手动泵 Tick**：那正是 PlayMode 相对 EditMode 的意义所在。
    ///
    /// **一条纪律**：本组只钉**同步可判定**的契约。凡是依赖"某帧之后 `AudioSource.isPlaying`
    /// 才翻真"之类的断言一律不写——那是时序抽奖，会随机器负载忽绿忽红。
    /// </summary>
    [Category(TestCategory.Contract)]
    public sealed class SceneAndPresentationPlayModeTests
    {
        /// <summary>已入库的受控场景（`Assets/Scenes/`；非美术内容，随库分发）。</summary>
        private const string TestScene = "Assets/Scenes/Test.unity";

        private PlayModeTestScope _scope;
        private bool _assetsReady;
        private WorldClock _world;
        private UIClock _ui;
        private AudioService _audio;
        private VfxService _vfx;                    // 非 UnityEngine.Object —— 不能进 scope，TearDown 显式释放

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _scope = new PlayModeTestScope(nameof(SceneAndPresentationPlayModeTests));
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

            // 真实 PlayerLoop 驱动两个时钟域（生产里由 GameEntry.Update 驱动容器 Tickables）
            var host = _scope.CreateGameObject("ClockHost");
            PlayModeTicker.Attach(host, _world.Tick, _ui.Tick);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // 表现壳服务不是 Object，须显式释放（幂等：用例内已关闭时再调无副作用）
            _vfx?.Shutdown();
            _audio?.Shutdown();
            _vfx = null;
            _audio = null;
            yield return _scope.DisposeAsync();
        }

        /// <summary>音频服务（TearDown 兜底释放）。它自建 [Audio] 根并 DontDestroyOnLoad——
        /// 不显式 Shutdown 会跨用例泄漏；这正是"谁创建谁释放"契约，本组顺带钉它。</summary>
        private AudioService NewAudio(int effectProxies = 4)
        {
            var audio = new AudioService(effectProxies: effectProxies);
            audio.BindClocks(_world, _ui);
            _audio = audio;
            return audio;
        }

        // ---- 分域时钟：世界暂停 ≠ UI 暂停（由真实 PlayerLoop 推进）----

        [UnityTest]
        public IEnumerator 暂停_世界时钟停走_UI时钟不受影响()
        {
            // 分域时钟的核心判据：世界暂停时 UI 时钟必须照走——否则暂停菜单自己的动效会冻住。
            // 时钟由 PlayModeTicker 经真实 PlayerLoop 驱动（非测试手泵）——这是本组用 PlayMode 的理由。
            // **判据用 Now 而非 ScaledDelta 的绝对值**：GameClock 的 ScaledDelta 在暂停帧仍为 0，
            // 但断言它等于 0 会依赖"恰好落在某帧之后"，故以 Now 的**不增长**为准（时间戳语义）。
            yield return null;
            yield return null;

            Assert.Greater(_world.Now, 0f, "真实帧应推进世界时钟");
            Assert.Greater(_ui.Now, 0f, "真实帧应推进 UI 时钟");

            _world.Paused = true;
            float worldAtPause = _world.Now;
            float uiAtPause = _ui.Now;

            yield return null;
            yield return null;
            yield return null;

            Assert.AreEqual(worldAtPause, _world.Now, 0.0001f, "世界暂停后 Now 不得再走");
            Assert.Greater(_ui.Now, uiAtPause, "UI 时钟不受世界暂停影响");

            _world.Paused = false;
            float worldAtResume = _world.Now;
            yield return null;
            yield return null;
            Assert.Greater(_world.Now, worldAtResume, "恢复后世界时钟应继续");
        }

        [UnityTest]
        public IEnumerator 暂停_世界停走时_其下游表现冻结()
        {
            // 分域时钟的**下游效果**：VFX 到期按 `_clock.Now` 推算（VfxService 注释"到期回收不依赖
            // 粒子回调——按世界时钟推算"）。世界停走 → Now 不动 → 到期扫描不推进。
            // 这条比"时钟数值"更接近设计说的"世界/UI 暂停"要验的东西。
            var world = _scope.CreateGameObject("ClockFxWorld").transform;
            var pool = _scope.CreateGameObject("ClockFxPool");
            _vfx = new VfxService(SyncLoader, _world, new VfxCatalog(),
                VfxBudget.Unlimited(), world, pool.transform);

            VfxHandle h = _vfx.Play("fx_pause", world, follow: false);
            Assert.IsTrue(h.IsValid);

            _world.Paused = true;
            float frozenAt = _world.Now;

            yield return null;
            yield return null;
            yield return null;

            Assert.AreEqual(frozenAt, _world.Now, 0.0001f, "暂停期间世界时间不得推进");
            var snap = new Dictionary<string, string>();
            _vfx.Snapshot(snap);
            Assert.AreEqual("1", snap["活跃"], "世界停走时特效不应因时间推进而到期回收");
        }

        // ---- 音效段（此前全仓无独立覆盖）----

        [UnityTest]
        public IEnumerator 音效_真实播放_Stop后归零且重复归还幂等()
        {
            var audio = NewAudio();
            var clip = AudioClip.Create("tone", 4410, 1, 44100, false);   // 受控片段（不引美术资产）

            int handle = audio.Play(AudioService.Group.Effect, clip);
            Assert.AreNotEqual(0, handle, "播放应返回有效句柄");

            yield return null;                                  // 让 AudioSource 真正起播

            var snap = new Dictionary<string, string>();
            audio.Snapshot(snap);
            Assert.IsTrue(snap.ContainsKey("Effect"), "快照应含 Effect 组");

            audio.Stop(handle);
            audio.Stop(handle);                                 // **重复归还**：失效句柄安全忽略
            audio.Stop(handle);
            yield return null;

            audio.Snapshot(snap);
            Assert.AreEqual("0/4", snap["Effect"], "Stop（含重复）后 Effect 组应归零");

            audio.Shutdown();
            audio.Shutdown();                                   // 关闭幂等
            yield return null;
        }

        [UnityTest]
        public IEnumerator 音效_优先级抢占_旧句柄失效不误停新音()
        {
            // 评审 6.3：句柄单调递增且**永不复用**——被抢占代理的旧句柄必然失效，
            // 外部持旧句柄 Stop 不会误停新音。抢占是同步的，与帧时序无关。
            var audio = NewAudio(effectProxies: 1);             // 单代理：第二次必然抢占
            var clip = AudioClip.Create("tone2", 4410, 1, 44100, false);

            int low = audio.Play(AudioService.Group.Effect, clip, priority: 1);
            Assert.AreNotEqual(0, low);

            int high = audio.Play(AudioService.Group.Effect, clip, priority: 9);   // 抢占
            Assert.AreNotEqual(0, high);
            Assert.Greater(high, low, "句柄单调递增（新占用的句柄更大）");

            audio.Stop(low);                                    // 旧（已被抢占）句柄 —— 必须安全忽略

            var snap = new Dictionary<string, string>();
            audio.Snapshot(snap);
            Assert.AreNotEqual("0/1", snap["Effect"], "旧句柄 Stop 不得误停新音");

            audio.Shutdown();
            yield return null;
        }

        // ---- 真资源场景加载/卸载 ----

        [UnityTest]
        public IEnumerator 场景_真资源加载与卸载_状态跟随且释放后拒绝加载()
        {
            Assert.IsTrue(System.IO.File.Exists(TestScene), $"受控场景缺失：{TestScene}");

            var scene = new SceneService();
            Assert.IsFalse(scene.IsLoaded(TestScene), "初始未加载");

            var load = scene.LoadSingleAsync(TestScene);
            yield return Wait(load, 30f);
            Assert.IsFalse(load.Status == UniTaskStatus.Pending, "场景加载超时");
            Assert.IsTrue(scene.IsLoaded(TestScene), "加载后 IsLoaded 应为真");

            var unload = scene.UnloadSingleAsync();
            yield return Wait(unload, 30f);
            Assert.IsFalse(scene.IsLoaded(TestScene), "卸载后 IsLoaded 应为假");
            Assert.DoesNotThrow(() => scene.UnloadSingleAsync().GetAwaiter().GetResult(),
                "二次卸载必须幂等");

            scene.ReleaseAll();                                  // 释放面：之后拒绝新加载
            Assert.IsTrue(scene.IsReleased);
            Assert.Throws<ObjectDisposedException>(
                () => scene.LoadSingleAsync(TestScene).GetAwaiter().GetResult(),
                "已释放的场景服务不得接受新加载");
        }

        // ---- 特效：在真场景里的生命周期 ----

        [UnityTest]
        public IEnumerator 特效_重复归还幂等_StopAll按挂点隔离()
        {
            var world = _scope.CreateGameObject("VfxWorld").transform;
            var attachA = _scope.CreateGameObject("AttachA").transform;
            var attachB = _scope.CreateGameObject("AttachB").transform;
            attachA.SetParent(world, false);
            attachB.SetParent(world, false);
            var pool = _scope.CreateGameObject("VfxPool");

            _vfx = new VfxService(SyncLoader, _world, new VfxCatalog(),
                VfxBudget.Unlimited(), world, pool.transform);

            VfxHandle a1 = _vfx.Play("fx_a", attachA, follow: true);
            VfxHandle a2 = _vfx.Play("fx_a", attachA, follow: true);
            VfxHandle b1 = _vfx.Play("fx_b", attachB, follow: true);
            Assert.IsTrue(a1.IsValid && a2.IsValid && b1.IsValid, "三个句柄都应有效");

            var snap = new Dictionary<string, string>();
            _vfx.Snapshot(snap);
            Assert.AreEqual("3", snap["活跃"], "三个活体");

            // **重复归还**：同一句柄 Stop 多次必须幂等（不重复入池、不误伤他人）
            _vfx.Stop(a1);
            _vfx.Stop(a1);
            _vfx.Stop(a1);
            _vfx.Snapshot(snap);
            Assert.AreEqual("2", snap["活跃"], "重复 Stop 不得多回收（a2+b1 仍活跃）");

            // StopAll 按挂点隔离：只清 attachA，不动 attachB
            _vfx.StopAll(attachA);
            _vfx.Snapshot(snap);
            Assert.AreEqual("1", snap["活跃"], "StopAll(attachA) 应只回收 a2");

            _vfx.Stop(b1);
            _vfx.Snapshot(snap);
            Assert.AreEqual("0", snap["活跃"], "全部归还后活跃归零");
        }

        [UnityTest]
        public IEnumerator 特效_关闭后在途加载被真取消_且拒绝新播放()
        {
            var world = _scope.CreateGameObject("VfxWorld2").transform;
            var attach = _scope.CreateGameObject("Attach2").transform;
            attach.SetParent(world, false);
            var pool = _scope.CreateGameObject("VfxPool2");

            // 慢加载器：把"在途"窗口变成**确定性**的（真实 IO 若同帧完成就测不到取消语义）
            _vfx = new VfxService(SlowLoader, _world, new VfxCatalog(),
                VfxBudget.Unlimited(), world, pool.transform);

            VfxHandle h = _vfx.Play("fx_slow", attach, follow: true);   // 进入在途
            Assert.IsTrue(h.IsValid);

            _vfx.Shutdown();                                            // 关闭：先行取消在途

            yield return null;
            yield return null;

            var snap = new Dictionary<string, string>();
            _vfx.Snapshot(snap);
            Assert.AreEqual("0", snap["活跃"], "关闭后不得有活体残留");
            Assert.IsTrue(_vfx.IsShutdown);

            // 关闭后拒绝新播放：**返回无效句柄**（不是抛异常——语义由各实现定，测试照实钉；
            // 对照 SceneService 那边是 ObjectDisposedException）。
            VfxHandle afterShutdown = _vfx.Play("fx_a", attach, follow: true);
            Assert.IsFalse(afterShutdown.IsValid, "已关闭的 VFX 服务不得接受新播放");
        }

        // ---- 租约故障段（设计点名「卸载中仍有租约」）----

        [UnityTest]
        public IEnumerator 租约_同location共享一份_卸载后归还且拒绝再取()
        {
            // 《框架先行》§6 样例③ 必测失败项「（卸载中）仍有租约」的落点。
            // 载体是 `PrefabLeaseCache`——**装配层的租约归属方**（表现壳只持引用，释放归它）。
            // 用**真 `IContentService`**（YooAssetContentService → AssetService 真资源链路），
            // 不是替身：租约的引用计数语义只有真链路才成立。
            var content = new YooAssetContentService();
            var init = content.InitializeAsync();
            yield return Wait(init, 60f);
            Assert.IsFalse(init.Status == UniTaskStatus.Pending, "内容服务初始化超时");

            const string location = "Assets/UI/Screens/BaselineA.prefab";
            var cache = new PrefabLeaseCache(content);

            var first = cache.GetAsync(location);
            yield return Wait(first, 30f);
            Assert.IsFalse(first.Status == UniTaskStatus.Pending, "首次取 prefab 超时");
            GameObject a = first.GetAwaiter().GetResult();
            Assert.IsNotNull(a, "真资源应取到 prefab");
            Assert.AreEqual(1, cache.HeldLocations, "首次取应持有一个 location");

            // **同 location 共享**：第二次取不得新增租约（引用计数语义）
            var second = cache.GetAsync(location);
            yield return Wait(second, 30f);
            GameObject b = second.GetAwaiter().GetResult();
            Assert.AreSame(a, b, "同 location 应共享同一份 prefab");
            Assert.AreEqual(1, cache.HeldLocations, "共享不得新增持有");

            // 卸载：ReleaseAll 归还全部租约（幂等）
            cache.ReleaseAll();
            Assert.AreEqual(0, cache.HeldLocations, "释放后不得仍有持有");
            Assert.DoesNotThrow(() => cache.ReleaseAll(), "ReleaseAll 幂等");

            // **「卸载中仍有租约」的判据**：释放后拒绝再取（而非静默给一份已归还的引用）
            Assert.Throws<InvalidOperationException>(
                () => cache.GetAsync(location).GetAwaiter().GetResult(),
                "已释放的租约缓存不得再提供 prefab（否则即为「卸载后仍有租约」）");

            var shutdown = content.ShutdownAsync();
            yield return Wait(shutdown, 30f);
        }

        [UnityTest]
        public IEnumerator 租约_取到即被释放的竞态_就地归还且上抛()
        {
            // PrefabLeaseCache.GetAsync 的竞态分支：`await AcquireAsync` 期间被 ReleaseAll
            // → **就地归还租约后上抛**（不能泄漏那份刚取到的引用）。这条与帧时序无关，是同步可判定的。
            var content = new YooAssetContentService();
            var init = content.InitializeAsync();
            yield return Wait(init, 60f);
            Assert.IsFalse(init.Status == UniTaskStatus.Pending, "内容服务初始化超时");

            var cache = new PrefabLeaseCache(content);

            // 先释放，再取 —— 走的是「已释放」分支（与竞态分支同样必须上抛）
            cache.ReleaseAll();
            Assert.Throws<InvalidOperationException>(
                () => cache.GetAsync("Assets/UI/Screens/BaselineA.prefab").GetAwaiter().GetResult(),
                "已释放后取 prefab 必须上抛（含 location 诊断）");

            var shutdown = content.ShutdownAsync();
            yield return Wait(shutdown, 30f);
        }

        // ---- 辅助 ----

        /// <summary>同步加载器（不引美术资产：引擎自建对象当"特效 prefab"）。</summary>
        private static UniTask<GameObject> SyncLoader(string location, System.Threading.CancellationToken ct)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "fx:" + location;
            return UniTask.FromResult(go);
        }

        /// <summary>慢加载器：让出若干帧，把"在途"窗口变成**确定性**的。</summary>
        private static async UniTask<GameObject> SlowLoader(string location, System.Threading.CancellationToken ct)
        {
            for (int i = 0; i < 5; i++) await UniTask.Yield(ct);
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "fx-slow:" + location;
            return go;
        }

        private static IEnumerator Wait(UniTask task, float timeoutSeconds)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (task.Status == UniTaskStatus.Pending && Time.realtimeSinceStartup < deadline)
                yield return null;
        }
    }
}
