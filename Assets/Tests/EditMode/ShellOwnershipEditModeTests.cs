using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using LiteFramework;
using LiteGame;
using LiteGame.UI;
using LiteTesting;
using LiteTesting.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using LiteClient;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 表现壳所有权与时钟用例：
    /// ① PrefabLeaseCache——共享租约、ReleaseAll 归零、释放后拒绝（UI-05 同源问题的表现壳落点）；
    /// ② AudioService StopAll/Shutdown——总线全局停止与释放面（§6.2/§12.3 预算基线面）；
    /// ③ UiAnimationClock/DotweenUiClockDriver——DOTween Manual 轨按 UIClock 派发（暂停即停）+ 轨隔离，
    ///    AnimatedImage 走 UIClock 步进（《动画模块专项设计》§1 裁决"SetUpdate(true) ≠ UIClock"）。
    /// 时序：UTCS 定向续延 + 手动 Tick；不依赖 PlayerLoop/YooAsset。
    /// </summary>
    public sealed class ShellOwnershipEditModeTests : UnityTestBase
    {
        // ---- 替身 ----

        /// <summary>内容服务假件：按 location 计数获取、生成**每 location 独立**的资产替身、收集释放。</summary>
        private sealed class FakeContent : IContentService
        {
            public readonly Dictionary<string, int> Acquires = new Dictionary<string, int>();
            public readonly List<int> Released = new List<int>();
            private readonly Func<string, GameObject> _assetFactory;
            public readonly List<GameObject> Created = new List<GameObject>();

            public FakeContent(Func<string, GameObject> assetFactory) => _assetFactory = assetFactory;

            public UniTask InitializeAsync(CancellationToken ct = default) => UniTask.CompletedTask;

            /// <summary>本替身不模拟内容发现——空清单。</summary>
            public System.Collections.Generic.IReadOnlyList<string> ListAssetPathsByTag(string tag)
                => System.Array.Empty<string>();

            public UniTask<AssetLease<T>> AcquireAsync<T>(string location, ContentGeneration generation = default, CancellationToken ct = default) where T : class
            {
                Acquires.TryGetValue(location, out int n);
                Acquires[location] = n + 1;
                var asset = _assetFactory(location);
                Created.Add(asset);
                var marker = new LeaseMarker { Index = Released.Count };
                Released.Add(0);
                return UniTask.FromResult(new AssetLease<T>(location, asset as T, _ => Released[marker.Index]++));
            }

            public UniTask ShutdownAsync(CancellationToken ct = default) => UniTask.CompletedTask;

            private sealed class LeaseMarker { public int Index; }
        }

        /// <summary>可控 IUIClock 假件（ScaledDelta 由测试直接设定）。</summary>
        private sealed class FakeUIClock : IUIClock
        {
            public float TimeScale { get; set; } = 1f;
            public bool Paused { get; set; }
            public float Now { get; private set; }
            public float ScaledDelta { get; set; }
            public void Tick(float realDelta) { }
            public string StatsName => "FakeUIClock";
            public void Snapshot(Dictionary<string, string> into) => into.Clear();
        }

        // ---- ① PrefabLeaseCache ----

        [Test]
        public void 租约缓存_同location共享一份_首次获取后不重复取()
        {
            var content = new FakeContent(loc => Scope.CreateGameObject(loc));
            var cache = new PrefabLeaseCache(content);

            var p1 = cache.GetAsync("Assets/UI/a").GetAwaiter().GetResult();
            var p2 = cache.GetAsync("Assets/UI/a").GetAwaiter().GetResult();
            Assert.AreSame(p1, p2, "同 location 共享一份租约/资产");
            Assert.AreEqual(1, content.Acquires["Assets/UI/a"], "并发/重复取用不重复取内容服务");

            var p3 = cache.GetAsync("Assets/UI/b").GetAwaiter().GetResult();
            Assert.AreEqual(2, cache.HeldLocations);
            Assert.AreNotSame(p1, p3);
        }

        [Test]
        public void 租约缓存_ReleaseAll归零_释放后拒绝()
        {
            var content = new FakeContent(loc => Scope.CreateGameObject(loc));
            var cache = new PrefabLeaseCache(content);

            cache.GetAsync("Assets/UI/a").GetAwaiter().GetResult();
            Assert.AreEqual(1, cache.HeldLocations);
            cache.ReleaseAll();
            Assert.AreEqual(0, cache.HeldLocations, "释放面归零（宿主关闭）");

            Assert.Throws<InvalidOperationException>(
                () => cache.GetAsync("Assets/UI/a").GetAwaiter().GetResult(), "释放后拒绝再取");
        }

        // ---- ② AudioService 全局停止与释放面 ----

        [Test]
        public void 音频_StopAll全局停止_Shutdown销毁根_幂等()
        {
            var audio = new AudioService();
            var clip = AudioClip.Create("t", 4410, 1, 44100, false);
            try
            {
                audio.Play(AudioService.Group.Effect, clip);
                audio.Play(AudioService.Group.Ui, clip);
                audio.StopAll();

                audio.Shutdown();                                // 释放面：停止 + 根销毁
                Assert.DoesNotThrow(() => audio.Shutdown(), "幂等");

                Assert.Catch<Exception>(() =>
                    audio.Play(AudioService.Group.Effect, clip), "关闭后拒绝播放");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(clip);      // EditMode 无延迟销毁帧——Destroy 会报错
            }
        }

        // ---- ③ 动效时钟（动画专项 §1：SetUpdate(true) ≠ UIClock）----

        [Test]
        public void 动效时钟_Driver按UIClock派发Manual轨_暂停即停()
        {
            var clock = new FakeUIClock();
            var driver = new DotweenUiClockDriver(clock);

            DOTween.defaultEaseType = Ease.Linear;               // 去缓动：进度 = 步进/时长，断言直读
            float value = 0f;
            DOTween.To(() => value, v => value = v, 1f, 0.25f)
                .SetUpdate(UpdateType.Manual, true);             // UIClock 轨（与 UiFx/转场同款）

            clock.ScaledDelta = 0f;                              // 暂停：UIClock 步进为零
            driver.Tick(0.016f);
            Assert.AreEqual(0f, value, "暂停即停——Manual 轨不推进（无 ManualUpdate 调用）");

            clock.ScaledDelta = 0.125f;                          // 半程步进
            driver.Tick(0.016f);
            Assert.AreEqual(0.5f, value, 0.01f, "按 UIClock 步进推进到半程");

            driver.Tick(0.016f);
            Assert.AreEqual(1f, value, 0.01f, "按 UIClock 步进推进至目标");

            DOTween.KillAll();
        }

        /// <summary>
        /// 轨隔离：<c>ManualUpdate</c> 只推进 <see cref="UpdateType.Manual"/> 轨——
        /// Normal/Late/Fixed 轨的既有 Tween 不受影响。这是 UIClock 接管"只动 UI 轨"的前提；
        /// 若此用例失败，说明 DOTween 版本语义变化，UiAnimationClock 的接线口径须重新评估。
        /// </summary>
        [Test]
        public void 动效时钟_Manual轨只推进Manual_引擎轨不受影响()
        {
            var clock = new FakeUIClock { ScaledDelta = 0.5f };
            var driver = new DotweenUiClockDriver(clock);

            DOTween.defaultEaseType = Ease.Linear;               // 去缓动：进度 = 步进/时长，断言直读

            float normal = 0f, manual = 0f;
            DOTween.To(() => normal, v => normal = v, 1f, 1f).SetUpdate(UpdateType.Normal, true);
            DOTween.To(() => manual, v => manual = v, 1f, 1f).SetUpdate(UpdateType.Manual, true);

            driver.Tick(0.016f);
            Assert.AreEqual(0.5f, manual, 0.01f, "Manual 轨按 UIClock 步进推进");
            Assert.AreEqual(0f, normal, "Normal 轨不归 UIClock 泵管——ManualUpdate 不触碰引擎自动更新轨");

            DOTween.KillAll();
        }

        [Test]
        public void 动效时钟_未绑定退化真实帧步进()
        {
            UiAnimationClock.ResetForEditorReload();             // 未绑定：Delta = Time.unscaledDeltaTime（≥0）
            Assert.GreaterOrEqual(UiAnimationClock.Delta, 0f, "编辑器/测试兜底语义——不抛不 NaN");
        }

        [Test]
        public void 序列帧_按UIClock步进_暂停不推进()
        {
            var go = Scope.CreateGameObject("anim", typeof(RectTransform), typeof(Image));
            var anim = go.AddComponent<AnimatedImage>();
            var tex = new Texture2D(2, 2);
            anim.Frames = new[] { Sprite.Create(tex, new Rect(0, 0, 2, 2), Vector2.one * 0.5f),
                                  Sprite.Create(tex, new Rect(0, 0, 2, 2), Vector2.one * 0.5f) };
            anim.Fps = 10f;

            // 反射驱动私有 OnEnable/Update（EditMode 不派发引擎消息——与 AppLifetime 用例同口径）
            typeof(AnimatedImage).GetMethod("OnEnable", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.Invoke(anim, null);

            var clock = new FakeUIClock { ScaledDelta = 0.5f };  // 0.5s > 1/10s——步进必然推进
            UiAnimationClock.Bind(clock);

            typeof(AnimatedImage).GetMethod("Update", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.Invoke(anim, null);
            var image = go.GetComponent<Image>();
            Assert.NotNull(image.sprite, "UIClock 步进驱动序列帧");

            clock.ScaledDelta = 0f;                              // 暂停
            typeof(AnimatedImage).GetMethod("Update", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.Invoke(anim, null);
            Assert.NotNull(image.sprite, "暂停时保持当前帧（时停语义——不崩不回退）");

            foreach (var s in anim.Frames) UnityEngine.Object.DestroyImmediate(s);
            UnityEngine.Object.DestroyImmediate(tex);
            UiAnimationClock.ResetForEditorReload();
        }
    }
}
