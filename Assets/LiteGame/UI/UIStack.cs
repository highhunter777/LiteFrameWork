using System;
using System.Collections.Generic;

namespace LiteGame
{
    /// <summary>
    /// 组内界面栈（M4 §2.1）：开序即深序（尾 = 最上）；Top/Pop 语义供 §2.2 IPopInterceptor 消费。
    /// 仅登记"开着"的界面（Close 即出栈，复用再入栈）。
    /// </summary>
    public sealed class UIStack
    {
        private readonly List<UIForm> _open = new List<UIForm>(8);

        public int Count => _open.Count;
        public UIForm Top => _open.Count > 0 ? _open[_open.Count - 1] : null;
        public IReadOnlyList<UIForm> Open => _open;

        public void Push(UIForm form)
        {
            if (form == null) throw new ArgumentNullException(nameof(form));
            if (_open.Contains(form))
                throw new InvalidOperationException($"UIForm[{form.Id}] 重复入栈");
            _open.Add(form);
        }

        public bool Remove(UIForm form) => _open.Remove(form);
    }
}
