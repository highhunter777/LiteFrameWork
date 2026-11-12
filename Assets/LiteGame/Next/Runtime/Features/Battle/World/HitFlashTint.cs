using System.Collections.Generic;
using UnityEngine;

namespace LiteGameNext.Features.Battle.World
{
    /// <summary>受击反馈：HP 下降时全身材质泛红闪一下，随后渐隐回基色（纯表现，不参与判定）。</summary>
    public sealed class HitFlashTint : MonoBehaviour
    {
        private const float FlashSeconds = 0.18f;
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private readonly List<Renderer> _renderers = new List<Renderer>();
        private Color _baseColor = Color.white;
        private float _remaining;
        private int _lastHp = -1;

        private void Awake() => Collect();

        /// <summary>设定基色（本地绿/他人红），并应用到全部渲染器。</summary>
        public void SetBaseColor(Color color)
        {
            _baseColor = color;
            if (_renderers.Count == 0) Collect();
            ApplyBlend(0f);
        }

        /// <summary>HP 变化驱动：下降即触发一次闪烁。</summary>
        public void ObserveHp(int hp)
        {
            if (_lastHp >= 0 && hp < _lastHp) _remaining = FlashSeconds;
            _lastHp = hp;
        }

        private void Update()
        {
            if (_remaining <= 0f) return;
            _remaining = Mathf.Max(0f, _remaining - Time.deltaTime);
            ApplyBlend(_remaining / FlashSeconds);
        }

        private void Collect()
        {
            _renderers.Clear();
            GetComponentsInChildren(true, _renderers);
        }

        private void ApplyBlend(float hitAmount)
        {
            Color color = Color.Lerp(_baseColor, Color.red, hitAmount);
            var block = new MaterialPropertyBlock();
            for (int i = 0; i < _renderers.Count; i++)
            {
                Renderer renderer = _renderers[i];
                if (renderer == null) continue;
                renderer.GetPropertyBlock(block);
                block.SetColor(ColorId, color);
                block.SetColor(BaseColorId, color);
                renderer.SetPropertyBlock(block);
            }
        }
    }
}
