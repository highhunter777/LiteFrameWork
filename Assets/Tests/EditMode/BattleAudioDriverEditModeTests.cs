using System;
using Cysharp.Threading.Tasks;
using LiteClient;
using LiteSim;
using LiteTesting;
using LiteView;
using NUnit.Framework;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 对局音效驱动（<see cref="BattleAudioDriver"/>）EditMode 覆盖：帧事件 → kind 到 clip 的路由矩阵、
    /// 缺 clip 降级（null clip 静默跳过不抛）、Dispose 后不路由。路由计数为观察面
    /// （播放本体归 AudioService 的组+代理模型——丢弃与抢占是其内建语义；EditMode 无音频输出，不播出断言）。
    /// </summary>
    [Category(TestCategory.Contract)]
    public sealed class BattleAudioDriverEditModeTests
    {
        private static AudioClip Clip(string name) => AudioClip.Create(name, 8, 1, 8000, false);

        private static HitFeedbackContext Ctx(FrameEventKind kind, HitLocalRole role, long entityId = 65537L)
            => new HitFeedbackContext(kind, entityId, 0L, 0, -1, 10, new Vector3(1f, 2f, 3f), role);

        [Test]
        public void 路由矩阵_五类事件全路由_计数递增()
        {
            var audio = new AudioService();
            try
            {
                using (var driver = new BattleAudioDriver(audio, Clip("f"), Clip("h"), Clip("c"), Clip("d")))
                {
                    driver.OnHitFeedback(Ctx(FrameEventKind.Fire, HitLocalRole.Caused));
                    driver.OnHitFeedback(Ctx(FrameEventKind.Fire, HitLocalRole.Bystander));
                    driver.OnHitFeedback(Ctx(FrameEventKind.Hit, HitLocalRole.Caused));
                    driver.OnHitFeedback(Ctx(FrameEventKind.Crit, HitLocalRole.Caused));
                    driver.OnHitFeedback(Ctx(FrameEventKind.Death, HitLocalRole.Bystander));
                    Assert.AreEqual(5, driver.RoutedCount, "五类事件全部路由");
                }
            }
            finally { audio.Shutdown(); }
        }

        [Test]
        public void 缺clip_对应kind静默跳过_其余正常路由()
        {
            var audio = new AudioService();
            try
            {
                using (var driver = new BattleAudioDriver(audio, fire: null, hit: Clip("h"), crit: null, death: Clip("d")))
                {
                    driver.OnHitFeedback(Ctx(FrameEventKind.Fire, HitLocalRole.Caused));    // fire 缺 → 静默跳过
                    driver.OnHitFeedback(Ctx(FrameEventKind.Hit, HitLocalRole.Caused));     // hit 在 → 路由
                    driver.OnHitFeedback(Ctx(FrameEventKind.Crit, HitLocalRole.Caused));    // crit 缺 → 静默跳过
                    Assert.AreEqual(3, driver.RoutedCount, "缺 clip 的 kind 不路由，其余正常");
                }
            }
            finally { audio.Shutdown(); }
        }

        [Test]
        public void Dispose后_事件不再路由()
        {
            var audio = new AudioService();
            try
            {
                var driver = new BattleAudioDriver(audio, Clip("f"), Clip("h"), Clip("c"), Clip("d"));
                driver.Dispose();
                driver.OnHitFeedback(Ctx(FrameEventKind.Fire, HitLocalRole.Caused));
                Assert.AreEqual(0, driver.RoutedCount, "Dispose 后不路由");
            }
            finally { audio.Shutdown(); }
        }
    }
}
