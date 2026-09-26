// 拆自 Display.cs（2026-09-14：一类一文件——非首个 MonoBehaviour 无法序列化进 prefab，实测）
using TMPro;
using System;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.UI
{
    /// <summary>倒计时（M4c）：unscaled 时钟驱动（UI 时层），OnDone 一次性回调。格式默认 mm:ss。
    /// Awake 惰性初始化 OnDone——运行时 AddComponent 不走反序列化，UnityEvent 字段为 null。</summary>
    public class Countdown : MonoBehaviour
    {
        public TMP_Text Label;
        public UnityEngine.Events.UnityEvent OnDone;

        private float _remaining;
        private bool _running;
        public bool Running => _running;

        protected virtual void Awake()
        {
            if (OnDone == null) OnDone = new UnityEngine.Events.UnityEvent();
        }

        public void StartCountdown(float seconds)
        {
            _remaining = seconds;
            _running = seconds > 0f;
            Render();
        }

        public void Stop() => _running = false;

        /// <summary>推进（Update 每帧喂 unscaled dt；公开供自测确定性推进）。</summary>
        public void Tick(float deltaTime)
        {
            if (!_running) return;
            _remaining -= deltaTime;
            if (_remaining <= 0f)
            {
                _remaining = 0f;
                _running = false;
                Render();
                OnDone?.Invoke();
                return;
            }
            Render();
        }

        private void Update() => Tick(Time.unscaledDeltaTime);

        private void Render()
        {
            if (Label == null) return;
            var t = TimeSpan.FromSeconds(Math.Max(0, _remaining));
            Label.text = $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";
        }
    }

}
