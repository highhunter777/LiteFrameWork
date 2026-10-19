using UnityEngine;

namespace LiteView
{
    /// <summary>
    /// 固定 16:9 信箱式视口（N1）：把 3D 渲染视口居中裁成 16:9，非 16:9 屏幕留黑边。
    /// 与只锁横向 FOV 的方案不同，视口本身是 16:9 ⇒ X/Y 可见范围在任意屏幕上完全一致。
    /// 纯数学，无场景/资源依赖；UI（ScreenSpaceOverlay）不受本视口影响。
    /// </summary>
    public static class CameraAspectLetterbox
    {
        /// <summary>构图基准画幅（UI 参考 1920×1080）。</summary>
        public const float ReferenceAspect = 16f / 9f;

        /// <summary>返回相机归一化视口（0..1）；非法屏幕尺寸回落全屏。</summary>
        public static Rect Viewport(int screenWidth, int screenHeight)
        {
            if (screenWidth <= 0 || screenHeight <= 0) return new Rect(0f, 0f, 1f, 1f);

            float aspect = screenWidth / (float)screenHeight;
            if (aspect > ReferenceAspect + 0.0001f)          // 超宽：左右黑边
            {
                float width = (screenHeight * ReferenceAspect) / screenWidth;
                return new Rect((1f - width) * 0.5f, 0f, width, 1f);
            }
            if (aspect < ReferenceAspect - 0.0001f)          // 过高：上下黑边
            {
                float height = (screenWidth / ReferenceAspect) / screenHeight;
                return new Rect(0f, (1f - height) * 0.5f, 1f, height);
            }
            return new Rect(0f, 0f, 1f, 1f);
        }
    }
}
