// 一类一文件：非首个 MonoBehaviour 无法序列化进 prefab
using UnityEngine;

namespace LiteGame.UI
{
    /// <summary>连续旋转装饰：按角速度绕 z 轴自转（加载转圈等**循环装饰**——同 <see cref="AnimatedImage"/>
    /// 口径：自播件直挂 prefab，不走 UiEffectBinder；依据《UI效果系统专项设计》§5.1"循环装饰不走本组件
    /// （自播件直接挂 prefab/AnimatedImage）"）。</summary>
    public class RotatingImage : MonoBehaviour
    {
        /// <summary>角速度（度/秒；正 = 屏幕系逆时针）。0 = 不动。</summary>
        public float DegreesPerSecond = 180f;

        public bool PlayOnEnable = true;

        private RectTransform _rect;
        private float _angle;
        private bool _playing;

        /// <summary>从 0° 重新起转。</summary>
        public void Play() { _angle = 0f; _playing = true; Apply(); }

        public void Stop() => _playing = false;

        private void OnEnable()
        {
            _rect = transform as RectTransform;
            if (PlayOnEnable) Play();
        }

        private void Update()
        {
            if (!_playing || _rect == null) return;
            // UIClock 步进（时停/暂停即停；未绑定退化真实帧）；角度取模防长跑精度漂移
            _angle = Mathf.Repeat(_angle + DegreesPerSecond * UiAnimationClock.Delta, 360f);
            Apply();
        }

        private void Apply()
        {
            if (_rect != null) _rect.localRotation = Quaternion.Euler(0f, 0f, _angle);
        }
    }

}
