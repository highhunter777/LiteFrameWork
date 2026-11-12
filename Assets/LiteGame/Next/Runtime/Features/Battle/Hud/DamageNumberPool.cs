using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGameNext.Features.Battle.Hud
{
    /// <summary>伤害数字飘字池：世界位 → 屏幕，上浮淡出；实例池复用，容器缺失静默跳过。</summary>
    public sealed class DamageNumberPool
    {
        /// <summary>同时存在的飘字上限（超出复用最旧）。</summary>
        public const int MaxLive = 16;

        /// <summary>单条飘字存活时长（秒）。</summary>
        public const float LifetimeSeconds = 0.8f;

        /// <summary>上浮距离（像素）。</summary>
        public const float RisePixels = 60f;

        private sealed class Live
        {
            public TextMeshProUGUI Label;
            public Vector3 World;
            public float SpawnedAt;
            public bool Active;
        }

        private readonly List<Live> _pool = new List<Live>(MaxLive);
        private readonly RectTransform _container;
        private Camera _camera;

        /// <param name="container">飘字容器（HUD 的 Cot_DamageNumbers）；null = 全部 no-op。</param>
        public DamageNumberPool(RectTransform container) => _container = container;

        /// <summary>当前存活条数（诊断/测试）。</summary>
        public int LiveCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _pool.Count; i++)
                    if (_pool[i].Active) count++;
                return count;
            }
        }

        /// <summary>池容量（诊断/测试）。</summary>
        public int PoolCount => _pool.Count;

        /// <summary>压入一条飘字。</summary>
        public void Push(string text, Vector3 world, Color color)
        {
            if (_container == null || string.IsNullOrEmpty(text)) return;
            Live slot = Acquire();
            slot.World = world;
            slot.SpawnedAt = Time.realtimeSinceStartup;
            slot.Active = true;
            slot.Label.text = text;
            slot.Label.color = color;
            slot.Label.gameObject.SetActive(true);
            Reposition(slot, 0f);
        }

        /// <summary>逐帧推进：上浮 + 淡出 + 回收。</summary>
        public void Tick()
        {
            if (_container == null) return;
            float now = Time.realtimeSinceStartup;
            for (int i = 0; i < _pool.Count; i++)
            {
                Live slot = _pool[i];
                if (!slot.Active) continue;
                float age = now - slot.SpawnedAt;
                if (age >= LifetimeSeconds)
                {
                    slot.Active = false;
                    slot.Label.gameObject.SetActive(false);
                    continue;
                }
                Reposition(slot, age / LifetimeSeconds);
            }
        }

        private void Reposition(Live slot, float progress)
        {
            if (_camera == null) _camera = Camera.main;
            Vector3 screen = _camera != null
                ? _camera.WorldToScreenPoint(slot.World)
                : new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f);
            if (screen.z < 0f)
            {
                slot.Label.gameObject.SetActive(false);
                return;
            }
            slot.Label.rectTransform.position = screen + new Vector3(0f, progress * RisePixels, 0f);
            Color color = slot.Label.color;
            color.a = 1f - progress;
            slot.Label.color = color;
        }

        private Live Acquire()
        {
            for (int i = 0; i < _pool.Count; i++)
                if (!_pool[i].Active) return _pool[i];

            if (_pool.Count >= MaxLive)
            {
                Live oldest = _pool[0];
                for (int i = 1; i < _pool.Count; i++)
                    if (_pool[i].SpawnedAt < oldest.SpawnedAt) oldest = _pool[i];
                return oldest;
            }

            var go = new GameObject("Dmg", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(_container, false);
            var label = go.GetComponent<TextMeshProUGUI>();
            label.fontSize = 42f;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            go.SetActive(false);
            var slot = new Live { Label = label };
            _pool.Add(slot);
            return slot;
        }
    }
}
