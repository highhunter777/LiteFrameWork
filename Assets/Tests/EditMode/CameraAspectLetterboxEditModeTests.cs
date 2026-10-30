using LiteGame.View;
using NUnit.Framework;
using UnityEngine;
using LiteFramework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>固定 16:9 信箱视口的纯数学验收（N1）：X/Y 可见范围任意屏幕一致。</summary>
    public sealed class CameraAspectLetterboxEditModeTests
    {
        private const float Eps = 1e-4f;

        private static float PixelAspect(int width, int height, Rect viewport)
            => (width * viewport.width) / (height * viewport.height);

        [Test]
        public void 十六比九_全屏()
        {
            Rect v = CameraAspectLetterbox.Viewport(1920, 1080);
            Assert.AreEqual(0f, v.x, Eps);
            Assert.AreEqual(0f, v.y, Eps);
            Assert.AreEqual(1f, v.width, Eps);
            Assert.AreEqual(1f, v.height, Eps);
        }

        [Test]
        public void 四比三_上下黑边_视口仍为十六比九()
        {
            Rect v = CameraAspectLetterbox.Viewport(1024, 768);
            Assert.AreEqual(0f, v.x, Eps);
            Assert.AreEqual(0.75f, v.height, Eps);            // 1024/16*9 = 576 → 576/768
            Assert.AreEqual(0.125f, v.y, Eps);
            Assert.AreEqual(16f / 9f, PixelAspect(1024, 768, v), Eps);
        }

        [Test]
        public void 超宽_左右黑边_视口仍为十六比九()
        {
            Rect v = CameraAspectLetterbox.Viewport(2560, 1080);
            Assert.AreEqual(0.75f, v.width, Eps);             // 1080/9*16 = 1920 → 1920/2560
            Assert.AreEqual(0.125f, v.x, Eps);
            Assert.AreEqual(1f, v.height, Eps);
            Assert.AreEqual(16f / 9f, PixelAspect(2560, 1080, v), Eps);
        }

        [Test]
        public void 非法尺寸_回落全屏()
        {
            Assert.AreEqual(new Rect(0f, 0f, 1f, 1f), CameraAspectLetterbox.Viewport(0, 0));
        }
    }
}
