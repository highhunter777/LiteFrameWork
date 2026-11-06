﻿namespace LiteGame
{
    /// <summary>
    /// 相机瞄准混合门（最小保持宽度）：喂"玩家原始瞄准位"与帧间隔，输出**相机混合位**。
    /// </summary>
    public sealed class CameraAimBlendGate
    {
        private readonly float _minHoldSeconds;
        private float _onSeconds;          // 本轮按下的持续时长（**每个上升沿清零**——连点的累计时长不得
                                           // 误触"长按立即释放"分支，否则连点仍会漏出单帧翻转）。
        private float _deferRemaining;     // >0 = 推迟释放窗口剩余（短按后的补足倒计时）
        private bool _held;                // 门控后的瞄准态
        private bool _prevRaw;             // 上一帧原始位（上升沿检测）

        public CameraAimBlendGate(float minHoldSeconds)
            => _minHoldSeconds = minHoldSeconds < 0f ? 0f : minHoldSeconds;

        /// <summary>喂一帧：原始瞄准位（Sim 预测态）+ 帧间隔，返回门控后的相机混合位。</summary>
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

        /// <summary>
        /// 复位到"未按下"（**复活/换局**时用）：上一条命的瞄准态若还挂着，复活瞬间相机会被钉在
        /// 瞄准机上（表现为"复活了但镜头没对准"）。原始位同步清零——下一帧的按下才重新算上升沿。
        /// </summary>
        public void Reset()
        {
            _held = false;
            _prevRaw = false;
            _onSeconds = 0f;
            _deferRemaining = 0f;
        }
    }
}
