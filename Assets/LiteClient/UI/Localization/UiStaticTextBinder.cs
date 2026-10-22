using LiteFramework;
using UnityEngine;
using UnityEngine.UI;

namespace LiteClient.UI
{
    /// <summary>
    /// 页面静态文案绑定：扫描页面根下的 <see cref="LTextKeyRef"/>，在页面打开与语言变更时按 key 写入。
    /// 关闭时由 <see cref="UIForm"/> 退订——池化复用不得继续被旧语言事件刷新。
    ///
    /// 文本目标解析与受控 API 同序：TMP 优先、UGUI Text 回退。缺目标/空 key 只告警一次，
    /// 不阻断同页其他节点（内容缺失走 Log.Warning + 保持作者文本的降级口径）。
    /// </summary>
    public sealed class UiStaticTextBinder
    {
        private readonly LTextKeyRef[] _refs;
        private readonly bool[] _warned;
        private ILocalizationService _locale;

        public UiStaticTextBinder(Transform root)
        {
            _refs = root != null
                ? root.GetComponentsInChildren<LTextKeyRef>(true)
                : System.Array.Empty<LTextKeyRef>();
            _warned = new bool[_refs.Length];
        }

        public int Count => _refs.Length;

        /// <summary>
        /// 绑定语言服务并立即应用一次。同一实例重复调用会重新应用（页面复用后作者文本可能被外部改写），
        /// 但不会重复订阅。
        /// </summary>
        public void Bind(ILocalizationService localization)
        {
            if (!ReferenceEquals(_locale, localization))
            {
                Unbind();
                _locale = localization;
                if (_locale != null && _refs.Length > 0) _locale.OnLocaleChanged += OnLocaleChanged;
            }
            Apply();
        }

        /// <summary>解除语言订阅（页面关闭时由 UIForm 调用）。</summary>
        public void Unbind()
        {
            if (_locale != null) _locale.OnLocaleChanged -= OnLocaleChanged;
            _locale = null;
        }

        /// <summary>按当前语言重写全部静态文案；未绑定语言服务时保持作者文本。</summary>
        public void Apply()
        {
            if (_locale == null) return;
            for (int i = 0; i < _refs.Length; i++)
            {
                int index = i;
                SafeCall.Invoke(() => ApplyOne(index), $"UiStaticTextBinder[{_refs[index]?.name}]");
            }
        }

        private void OnLocaleChanged(string _) => Apply();

        private void ApplyOne(int index)
        {
            LTextKeyRef reference = _refs[index];
            if (reference == null) return;
            if (string.IsNullOrWhiteSpace(reference.Key))
            {
                WarnOnce(index, $"[UiStaticTextBinder] 节点 {reference.name} 的 LTextKeyRef.Key 为空——跳过");
                return;
            }

            string text = _locale.Get(reference.Key);
            if (reference.TryGetComponent<TMPro.TMP_Text>(out var tmp))
            {
                tmp.text = text;
                return;
            }
            if (reference.TryGetComponent<Text>(out var legacy))
            {
                legacy.text = text;
                return;
            }

            WarnOnce(index, $"[UiStaticTextBinder] 节点 {reference.name} 缺 TMP_Text/Text——静态文案无法写入");
        }

        private void WarnOnce(int index, string message)
        {
            if (_warned[index]) return;
            _warned[index] = true;
            Log.Warning(message, LogTag.UI);
        }
    }
}
