using System;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteSim;
using LiteTesting;
using LiteGame.View;
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

        private static HitFeedbackContext Ctx2(FrameEventKind kind, HitLocalRole role, int slot)
            => new HitFeedbackContext(kind, 65537L, 0L, slot, -1, 10, new Vector3(1f, 2f, 3f), role);

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
        public void 开火_本地同源重触发_他人逐槽位voice_代理预算恒一()
        {
            var audio = new AudioService();
            try
            {
                using (var driver = new BattleAudioDriver(audio, Clip("f"), Clip("h"), Clip("c"), Clip("d")))
                {
                    for (int i = 0; i < 10; i++)
                        driver.OnHitFeedback(Ctx(FrameEventKind.Fire, HitLocalRole.Caused));

                    Assert.AreEqual(10, audio.RetriggerCount, "本地十发 = 十次同源重触发");
                    Assert.AreEqual(1, audio.VoiceCount, "本地连发恒一个 voice——不占新代理（连发不吃代理预算）");

                    driver.OnHitFeedback(Ctx(FrameEventKind.Fire, HitLocalRole.Bystander));   // 他人开火（Slot=0）
                    Assert.AreEqual(2, audio.VoiceCount, "他人开火逐槽位一个 voice");
                    driver.OnHitFeedback(Ctx(FrameEventKind.Fire, HitLocalRole.Bystander));   // 同槽第二发
                    Assert.AreEqual(2, audio.VoiceCount, "同槽第二发复用该 voice（键域 = 槽位数）");

                    driver.OnHitFeedback(Ctx2(FrameEventKind.Fire, HitLocalRole.Bystander, -1));   // 槽位未解析
                    Assert.AreEqual(2, audio.VoiceCount, "槽位未解析走一次性 3D——不建 voice");
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
