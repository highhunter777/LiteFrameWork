#if UNITY_EDITOR
using LiteFramework.UI;
using UnityEngine;

namespace LiteGame.Adapters.Dev.Projection.Preview
{
    /// <summary>
    /// 预览安全区模拟策略（P2-3 B3）。
    /// </summary>
    public sealed class PreviewSafeAreaStrategy : ISafeAreaStrategy
    {
        private readonly int _deviceWidth;
        private readonly int _deviceHeight;
        private readonly float _left;
        private readonly float _top;
        private readonly float _right;
        private readonly float _bottom;

        public PreviewSafeAreaStrategy(int deviceWidth, int deviceHeight,
            float insetLeft, float insetTop, float insetRight, float insetBottom)
        {
            _deviceWidth = deviceWidth;
            _deviceHeight = deviceHeight;
            _left = insetLeft;
            _top = insetTop;
            _right = insetRight;
            _bottom = insetBottom;
        }

        /// <summary>当前屏幕像素空间的安全矩形（inset 按屏幕/设备分辨率缩放）。</summary>
        public Rect Resolve()
        {
            if (_deviceWidth <= 0 || _deviceHeight <= 0) return new Rect(0f, 0f, Screen.width, Screen.height);

            float scaleX = Screen.width / (float)_deviceWidth;
            float scaleY = Screen.height / (float)_deviceHeight;
            float left = Mathf.Max(0f, _left) * scaleX;
            float right = Mathf.Max(0f, _right) * scaleX;
            float top = Mathf.Max(0f, _top) * scaleY;
            float bottom = Mathf.Max(0f, _bottom) * scaleY;
            float width = Mathf.Max(0f, Screen.width - left - right);
            float height = Mathf.Max(0f, Screen.height - top - bottom);
            return new Rect(left, bottom, width, height);
        }
    }
}
#endif
