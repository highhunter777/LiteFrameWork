#if UNITY_EDITOR
using System.Collections.Generic;
using System.Collections.ObjectModel;
using TMPro;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// 预览字体缩放（P2-3 B3；总设计 §7.5 钦定档 1.0/1.15/1.3）：对页面内全部 TMP 的 fontSize 施加乘数，
    /// 记录原值；切档/退出时恢复（乘回逆数会吃浮点精度——**记录原值恢复**）。
    /// </summary>
    public sealed class PreviewFontScale
    {
        /// <summary>总设计 §7.5 钦定字体缩放档（驱动/矩阵共用，避免两份字面量）。</summary>
        public static IReadOnlyList<float> SupportedScales { get; } =
            new ReadOnlyCollection<float>(new[] { 1f, 1.15f, 1.3f });

        private readonly Dictionary<TMP_Text, float> _originals = new Dictionary<TMP_Text, float>();

        /// <summary>施加缩放（幂等：重复调用先恢复再按新档应用）。</summary>
        public void Apply(GameObject pageRoot, float scale)
        {
            Restore();
            if (pageRoot == null || scale <= 0f) return;

            foreach (TMP_Text text in pageRoot.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text == null) continue;
                _originals[text] = text.fontSize;
                text.fontSize = _originals[text] * scale;
            }
        }

        /// <summary>恢复全部原值（退出/切档共用；无记录时零动作）。</summary>
        public void Restore()
        {
            foreach (KeyValuePair<TMP_Text, float> entry in _originals)
            {
                if (entry.Key != null) entry.Key.fontSize = entry.Value;
            }
            _originals.Clear();
        }
    }
}
#endif
