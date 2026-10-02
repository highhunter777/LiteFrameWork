using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteGame;
using LiteTesting;
using LiteTesting.Unity;
using NUnit.Framework;
using UnityEngine;
using LiteClient;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 表现壳生命周期收敛验收（《框架先行建设与业务接入专项设计》§6 样例三"场景与表现"的失败清单：
    /// **世界/UI 暂停、重复归还、加载后 Owner 已退出**；§5 接缝 1"谁创建、谁取消、谁等待清理、谁释放资源"）。
    ///
    /// 覆盖"所有权与释放面"——Scene 的迟到加载作废、Entity 的实体作用域与关闭面、
    /// Audio 的分域时钟。
    /// </summary>
    public sealed class ShellLifecycleEditModeTests : UnityTestBase
    {
        // ---- ① Audio：分域时钟（世界暂停而 UI 正常）----

        /// <summary>可设定 ScaledDelta 的时钟替身。</summary>
        private sealed class FakeClock : IWorldClock, IUIClock
        {
            public float TimeScale { get; set; } = 1f;
            public bool Paused { get; set; }
            public float Now { get; private set; }
            public float ScaledDelta { get; set; }
            public void Tick(float realDelta) { Now += ScaledDelta; }
            public string StatsName => "FakeClock";
            public void Snapshot(Dictionary<string, string> into) => into.Clear();
        }

        [Test]
        public void 音频_绑定分域时钟后_StopAll与Shutdown仍幂等()
        {
            var world = new FakeClock { ScaledDelta = 1f / 60f };
            var ui = new FakeClock { ScaledDelta = 1f / 60f };
            var audio = new AudioService();
            audio.BindClocks(world, ui);

            var clip = AudioClip.Create("t", 4410, 1, 44100, false);
            try
            {
                audio.Play(AudioService.Group.Effect, clip);
                audio.Play(AudioService.Group.Bgm, clip);
                audio.StopAll();
                audio.Shutdown();
                audio.Shutdown();                       // 幂等

                Assert.Catch<Exception>(() => audio.Play(AudioService.Group.Effect, clip), "关闭后拒绝播放");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void 音频_未绑定时钟_退化为真实帧步进不抛()
        {
            var audio = new AudioService();             // 未 BindClocks：编辑器/测试兜底路径
            var clip = AudioClip.Create("t", 4410, 1, 44100, false);
            try
            {
                audio.Play(AudioService.Group.Effect, clip, volume: 1f, fadeIn: 0.05f);   // 触发淡变路径
                Assert.DoesNotThrow(() => audio.Shutdown());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(clip);
            }
        }

        // ---- ② Entity：实体作用域 + 关闭面 + 重复归还 ----

        /// <summary>即时加载口替身（可按 location 生产独立 prefab）。</summary>
        private sealed class FakeLoader
        {
            private readonly Dictionary<string, GameObject> _prefabs = new Dictionary<string, GameObject>();
            public int LoadCalls;

            public UniTask<GameObject> Load(string location, CancellationToken ct)
            {
                LoadCalls++;
                if (!_prefabs.TryGetValue(location, out var prefab))
                {
                    prefab = new GameObject(System.IO.Path.GetFileName(location));
                    _prefabs[location] = prefab;
                    ScopeObjects.Add(prefab);
                }
                return UniTask.FromResult(prefab);
            }

            /// <summary>替身造的 prefab（测试收尾统一销毁）。</summary>
            public readonly List<GameObject> ScopeObjects = new List<GameObject>();
        }

        [Test]
        public void 实体_实体作用域随回收取消_不留裸任务()
        {
            var loader = new FakeLoader();
            var service = new EntityService(loader.Load);
            var handle = service.ShowAsync("Assets/Prefab/A.prefab").GetAwaiter().GetResult();

            Assert.IsNotNull(handle, "池未命中 → 加载并显示");
            Assert.IsNotNull(handle.Scope, "实体持自有作用域（取消源）");
            Assert.IsFalse(handle.Scope.Token.IsCancellationRequested);

            CancellationToken token = handle.Scope.Token;
            service.Hide(handle.Id);

            Assert.IsTrue(token.IsCancellationRequested, "回收即取消实体上的工作（§5 接缝 1）");
        }

        [Test]
        public void 实体_重复归还幂等_不抛且计数留痕()
        {
            var loader = new FakeLoader();
            var service = new EntityService(loader.Load);
            var handle = service.ShowAsync("Assets/Prefab/A.prefab").GetAwaiter().GetResult();

            service.Hide(handle.Id);
            Assert.DoesNotThrow(() => service.Hide(handle.Id), "重复归还 = 幂等 no-op");
            Assert.DoesNotThrow(() => service.Hide(99999), "未知句柄 = 幂等 no-op");
            Assert.AreEqual(2, service.DuplicateReturns, "重复归还计数留痕（诊断可见）");
        }

        [Test]
        public void 实体_关闭面_取消在途加载_收回活体_排空池_幂等()
        {
            var loader = new FakeLoader();
            var service = new EntityService(loader.Load);

            var h1 = service.ShowAsync("Assets/Prefab/A.prefab").GetAwaiter().GetResult();
            service.ShowAsync("Assets/Prefab/B.prefab").GetAwaiter().GetResult();
            service.Hide(h1.Id);                                     // 归还一件进池
            Assert.Greater(service.PooledTotal, 0, "池中有闲置件");

            service.Shutdown();
            Assert.IsTrue(service.IsShutdown);
            Assert.AreEqual(0, service.PooledTotal, "关闭排空实例池");
            Assert.DoesNotThrow(() => service.Shutdown(), "幂等");
        }

        [Test]
        public void 实体_关闭面_带挂接链_级联回收不重复处理()
        {
            // 回归卡：Shutdown 按 _active 快照遍历，而级联回收会带走子件——
            // 不检查"是否仍在 _active"会对已回收句柄再走一次 HideInternal（键不存在 → 抛）。
            var loader = new FakeLoader();
            var service = new EntityService(loader.Load);

            var parent = service.ShowAsync("Assets/Prefab/P.prefab").GetAwaiter().GetResult();
            var child = service.ShowAsync("Assets/Prefab/C.prefab").GetAwaiter().GetResult();
            service.Attach(child.Id, parent.Id);

            Assert.DoesNotThrow(() => service.Shutdown(), "带挂接链的关闭必须走通（级联 + 快照去重）");
            Assert.AreEqual(0, service.ActiveCount, "活体归零");
            Assert.AreEqual(0, service.PooledTotal, "池已排空");
        }

        [Test]
        public void 实体_宿主作用域注入_实体作用域随宿主级联()
        {
            var loader = new FakeLoader();
            using (var host = new ClientScope("Match"))
            {
                var service = new EntityService(loader.Load) { HostScope = host };
                var handle = service.ShowAsync("Assets/Prefab/A.prefab").GetAwaiter().GetResult();
                CancellationToken token = handle.Scope.Token;

                host.Dispose();                                      // 宿主退出（离场）
                Assert.IsTrue(token.IsCancellationRequested, "宿主退出级联取消实体上的工作");
            }
        }

        // ---- ③ Scene：迟到加载（§9"迟到结果释放自己的租约，不能回写已回收对象"）----

        [Test]
        public void 场景_释放后拒绝新加载()
        {
            var scene = new SceneService();
            scene.ReleaseAll();

            Assert.IsTrue(scene.IsReleased);
            Assert.Throws<ObjectDisposedException>(
                () => scene.LoadSingleAsync("Assets/Scene/A.unity").GetAwaiter().GetResult(),
                "已释放的服务不得接受新加载");
            Assert.Throws<ObjectDisposedException>(
                () => scene.LoadAdditiveAsync("Assets/Scene/B.unity").GetAwaiter().GetResult());
        }

        [Test]
        public void 场景_释放幂等_且使在途加载过期()
        {
            var scene = new SceneService();
            CancellationToken lifetime = scene.LifetimeToken;
            Assert.IsFalse(lifetime.IsCancellationRequested);

            scene.ReleaseAll();
            Assert.IsTrue(lifetime.IsCancellationRequested, "释放级联取消宿主代次令牌（在途加载的取消源）");

            Assert.DoesNotThrow(() => scene.ReleaseAll(), "幂等");
            Assert.AreEqual(0, scene.DiscardedLateLoads, "本次无在途加载——不应有迟到丢弃");
        }

        [Test]
        public void 场景_未加载时卸载为幂等noop()
        {
            var scene = new SceneService();
            Assert.DoesNotThrow(() => scene.UnloadSingleAsync().GetAwaiter().GetResult());
            Assert.DoesNotThrow(() => scene.UnloadAdditiveAsync("Assets/Scene/X.unity").GetAwaiter().GetResult());
            Assert.IsNull(scene.SingleSceneName);
            Assert.AreEqual(0, scene.AdditiveCount);
        }
    }
}
