// 拆自 Display.cs（2026-09-14：一类一文件——非首个 MonoBehaviour 无法序列化进 prefab，实测）
using System;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.UI
{
    /// <summary>帧序列动画（M4c）：Sprite 数组按 fps 循环/单次播放（灰盒动图——序列帧 UI 元素）。</summary>
    public class AnimatedImage : MonoBehaviour
    {
        public Sprite[] Frames;
        public float Fps = 10f;
        public bool Loop = true;
        public bool PlayOnEnable = true;

        private Image _image;
        private int _frame;
        private float _timer;
        private bool _playing;

        public void Play() { _frame = 0; _timer = 0f; _playing = true; }
        public void Stop() => _playing = false;

        private void OnEnable() { _image = GetComponent<Image>(); if (PlayOnEnable) Play(); }

        private void Update()
        {
            if (!_playing || _image == null || Frames == null || Frames.Length == 0) return;
            _timer += UiAnimationClock.Delta;               // G1 动画时钟：UIClock 步进（时停/暂停即停；未绑定退化真实帧）
            float step = 1f / Mathf.Max(1f, Fps);
            while (_timer >= step)
            {
                _timer -= step;
                _frame++;
                if (_frame >= Frames.Length)
                {
                    if (!Loop) { _frame = Frames.Length - 1; _playing = false; break; }
                    _frame = 0;
                }
            }
            _image.sprite = Frames[_frame];
        }
    }

}
