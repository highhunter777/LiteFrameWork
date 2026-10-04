namespace LiteGame
{
    /// <summary>
    /// 瞄准态滞回门（最小保持宽度）：喂原始瞄准位与帧间隔，输出门控后的瞄准态。
    ///
    /// 语义（2026-10-04 裁决——频繁点按 ADS 相机来回抖动的输入侧治理）：
    /// - **上升沿立即生效**（按下即瞄准，不加延迟）；
    /// - **下降沿按时长分治**——瞄准持续 ≥ 最小保持宽度 → 释放立即生效（长按手感不变）；
    ///   不足 → 推迟到满宽度（短按也得到一次完整摆动，不再半途折返）；
    /// - **推迟窗口内再按 → 取消推迟重新武装**（连点期间瞄准持续，相机不随点按来回翻转；
    ///   点按停止后，窗口走完自然释放）。
    ///
    /// 宽度与 Cinemachine Brain 的 DefaultBlend（EaseInOut 0.3s）对齐：每个摆动至少完整
    /// 走完一次混合，点按频率再高也只有一次混合在跑——"从中间态重启混合"的振荡由此消除。
    /// dt 由调用方注入（FSM OnUpdate 的 elapseSeconds），本类不读时钟（确定性可测）。
    /// </summary>
    public sealed class AimHoldGate
    {
        private readonly float _minHoldSeconds;
        private float _onSeconds;          // 本轮按下的持续时长（**每个上升沿清零**——连点的累计时长不得
                                           // 误触"长按立即释放"分支，否则连点仍会漏出单帧翻转）
        private float _deferRemaining;     // >0 = 推迟释放窗口剩余（短按后的补足倒计时）
        private bool _held;                // 门控后的瞄准态
        private bool _prevRaw;             // 上一帧原始位（上升沿检测）

        public AimHoldGate(float minHoldSeconds)
            => _minHoldSeconds = minHoldSeconds < 0f ? 0f : minHoldSeconds;

        /// <summary>喂一帧：原始瞄准位（Sim 预测态）+ 帧间隔，返回门控后的瞄准态。</summary>
        public bool Feed(bool rawAiming, float deltaSeconds)
        {
            if (rawAiming && !_prevRaw) _onSeconds = 0f;   // 上升沿：新一轮按下，按持续时长从零起算
            _prevRaw = rawAiming;

            if (rawAiming)
            {
                _onSeconds += deltaSeconds;
                _deferRemaining = 0f;      // 保持/再按：取消推迟，重新武装
                _held = true;
                return true;
            }

            if (!_held) return false;

            if (_deferRemaining > 0f)
            {
                _deferRemaining -= deltaSeconds;   // 推迟窗口：倒计时归零才真正释放
                if (_deferRemaining > 0f) return true;
                EndHold();
                return false;
            }

            if (_onSeconds < _minHoldSeconds)
            {
                _deferRemaining = _minHoldSeconds - _onSeconds;   // 短按：推迟释放到满宽度
                return true;                                      // 本帧维持，下帧起倒数
            }

            EndHold();                     // 长按：释放立即生效
            return false;
        }

        private void EndHold()
        {
            _held = false;
            _onSeconds = 0f;
            _deferRemaining = 0f;
        }
    }
}
