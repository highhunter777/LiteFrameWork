#if UNITY_EDITOR
using System.Collections.Generic;
using LiteClient.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame
{
    /// <summary>
    /// 布局档静态文案的作者原串快照（P2-3 B3）：切回「作者文本」或退出预览时把 LTextKeyRef 目标
    /// 写回加载时的原文；不依赖 UIForm 生命周期，也不改运行时 UiStaticTextBinder 的 Bind(null) 语义。
    /// </summary>
    public sealed class PreviewAuthorText
    {
        private readonly List<Entry> _entries = new List<Entry>();

        private sealed class Entry
        {
            public TMP_Text Tmp;
            public Text Ugui;
            public string Text;
        }

        public PreviewAuthorText(GameObject root)
        {
            if (root == null) return;
            foreach (LTextKeyRef reference in root.GetComponentsInChildren<LTextKeyRef>(true))
            {
                if (reference == null) continue;
                if (reference.TryGetComponent<TMP_Text>(out TMP_Text tmp))
                {
                    _entries.Add(new Entry { Tmp = tmp, Text = tmp.text });
                }
                else if (reference.TryGetComponent<Text>(out Text legacy))
                {
                    _entries.Add(new Entry { Ugui = legacy, Text = legacy.text });
                }
            }
        }

        public void Restore()
        {
            foreach (Entry entry in _entries)
            {
                if (entry.Tmp != null) entry.Tmp.text = entry.Text;
                else if (entry.Ugui != null) entry.Ugui.text = entry.Text;
            }
        }
    }
}
#endif
