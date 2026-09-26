using UnityEngine;

namespace LiteGame
{
    /// <summary>SafeArea 接收器（M4 §2.6 刘海屏红线）：把避让矩形应用到自身 RectTransform 的 anchors。
    /// 低频轮询（0.5s）+ 尺寸变化即标脏，避免每帧计算；策略可换（SetStrategy）。
    /// 2026-09-14 重建：拆文件时同名覆盖事故后按旧程序集反射签名还原（五个私有字段与四个方法逐一对齐）。</summary>
    public sealed class SafeAreaReceiver : MonoBehaviour
    {
        private ISafeAreaStrategy _strategy;
        private RectTransform _rt;
        private float _nextCheck;
        private Vector2 _lastMin;
        private Vector2 _lastMax;
        private bool _dirty;

        /// <summary>换策略（默认 Screen.safeArea 原样）；立即重算。</summary>
        public void SetStrategy(ISafeAreaStrategy strategy)
        {
            _strategy = strategy;
            _dirty = true;
            Apply();
        }

        private void OnEnable()
        {
            _rt = transform as RectTransform;
            _dirty = true;
            Apply();
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextCheck) return;      // 低频轮询：分辨率/刘海变化不频繁
            _nextCheck = Time.unscaledTime + 0.5f;
            Apply();
        }

        private void OnRectTransformDimensionsChange()
        {
            _dirty = true;                                   // 尺寸变化即标脏（下一帧或轮询时生效）
        }

        private void Apply()
        {
            if (_rt == null) _rt = transform as RectTransform;
            if (_rt == null || Screen.width <= 0 || Screen.height <= 0) return;

            Rect safe = _strategy != null ? _strategy.Resolve() : Screen.safeArea;
            Vector2 min = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height);
            Vector2 max = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);

            if (!_dirty && min == _lastMin && max == _lastMax) return;   // 无变化不写（避免无意义布局重建）
            _lastMin = min;
            _lastMax = max;
            _dirty = false;

            _rt.anchorMin = min;
            _rt.anchorMax = max;
            _rt.offsetMin = Vector2.zero;
            _rt.offsetMax = Vector2.zero;
        }
    }

    /// <summary>SafeArea 策略（M4 §2.6，设计方案 §1.3 刘海屏红线）：解析避让矩形。默认 = Screen.safeArea 原样
    /// （桌面/编辑器 = 全屏；异形屏 = 真实安全区；横竖屏 / 分屏自动跟随）。</summary>
    public interface ISafeAreaStrategy
    {
        Rect Resolve();
    }

    public sealed class DefaultSafeAreaStrategy : ISafeAreaStrategy
    {
        public Rect Resolve() => Screen.safeArea;
    }
}
