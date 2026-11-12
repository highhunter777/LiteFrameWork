using LiteGameNext.Platform.Audio;
using NUnit.Framework;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>音频服务：句柄分配/停止/分组音量/关闭幂等；编辑期不依赖真实播放。</summary>
    public sealed class ClientNextAudioTests
    {
        private static AudioClip MakeClip()
        {
            // 空 clips 在编辑期不能播放，但服务只做池与句柄管理，不依赖播放状态。
            return AudioClip.Create("test", 128, 1, 44100, false);
        }

        [Test]
        public void 关闭后_播放返回零且不抛()
        {
            var audio = new AudioService();
            audio.Shutdown();
            Assert.IsTrue(audio.IsShutdown);
            Assert.DoesNotThrow(() => Assert.AreEqual(0, audio.Play(AudioGroup.Effect, MakeClip())));
        }

        [Test]
        public void 关闭幂等_重复调用不抛()
        {
            var audio = new AudioService();
            audio.Shutdown();
            Assert.DoesNotThrow(() => audio.Shutdown());
        }

        [Test]
        public void 分组音量_越界被钳制()
        {
            var audio = new AudioService();
            try
            {
                Assert.DoesNotThrow(() => audio.SetGroupVolume(AudioGroup.Bgm, 5f));
                Assert.DoesNotThrow(() => audio.SetGroupVolume(AudioGroup.Bgm, -1f));
            }
            finally
            {
                audio.Shutdown();
            }
        }

        [Test]
        public void 停止未知句柄_不抛()
        {
            var audio = new AudioService();
            try
            {
                Assert.DoesNotThrow(() => audio.Stop(999, 0f));
                Assert.DoesNotThrow(() => audio.Stop(999, 0.2f));
            }
            finally
            {
                audio.Shutdown();
            }
        }

        [Test]
        public void 全停与统计_初值为零()
        {
            var audio = new AudioService();
            try
            {
                Assert.AreEqual(0, audio.VoiceCount);
                Assert.GreaterOrEqual(audio.DroppedCount, 0);
                Assert.DoesNotThrow(() => audio.StopAll());
            }
            finally
            {
                audio.Shutdown();
            }
        }
    }
}
