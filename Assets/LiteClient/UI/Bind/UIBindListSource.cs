using System;
using System.Collections.Generic;
using LiteFramework;
using UnityEngine;
using UnityEngine.UI;

namespace LiteClient.UI
{
    /// <summary>
    /// 绑定索引的**列表域**：列表源登记、行 → 条目投影、行点击钩子、窗口重绑与池化解绑。
    /// 行文案的本地化解析经 <see cref="UIBindLocale"/>；控件查询与文本写入仍走索引的受控面。
    /// </summary>
    internal sealed class UIBindListSource
    {
        private readonly UIBindIndex _index;
        private readonly UIBindLocale _locale;
        private readonly Dictionary<string, BindListSource> _sources = new Dictionary<string, BindListSource>(4);
        private readonly Dictionary<string, Action<string, string>> _click = new Dictionary<string, Action<string, string>>(4);

        /// <summary>空行源（SetList 的“清空过渡”——内核 count≤0 即 ReleaseAll，随后换真源全部重绑）。</summary>
        private static readonly IUIListRows EmptyRows = new EmptyListRows();

        public UIBindListSource(UIBindIndex index, UIBindLocale locale)
        {
            _index = index ?? throw new ArgumentNullException(nameof(index));
            _locale = locale ?? throw new ArgumentNullException(nameof(locale));
        }

        /// <summary>
        /// 装配列表数据源（**整表替换语义**：更新/缩容同一入口——换数据再调一次即可，内核按新 Count 重算窗口）。
        /// 目标节点取其**自身或子级**的 VirtualList/SimpleListView（列表节点的组件住 Content 子级，绑定索引存的是
        /// 根上自动检测组件）。行含文案键而语言服务未注入 = 装配错误（fail-fast——不在滚动回调路径上抛）。G6。
        /// </summary>
        public void Set(string name, IUIListRows rows)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            _index.MarkDriver(name, ControlDriver.Command);
            GuardLocalizedRows(name, rows);

            var source = new BindListSource(this, name, rows);
            _sources[name] = source;
            // 空源过渡 → 新源：强迫已实体化条目**全部重绑**（内核只在索引进出窗口时绑定；
            // 整表替换时同一索引的数据已变，不重绑会显示旧行——缩容/更新同判据）
            var emptySource = new BindListSource(this, name, EmptyRows);
            if (_index.TryGetList<VirtualList>(name, out var virtualList))
            {
                virtualList.SetSource(emptySource);
                virtualList.SetSource(source);
                return;
            }
            if (_index.TryGetList<SimpleListView>(name, out var simpleView))
            {
                simpleView.SetSource(emptySource);
                simpleView.SetSource(source);
                return;
            }
            throw new InvalidOperationException(
                $"绑定索引[{name}] 无 VirtualList / SimpleListView 组件（SetList 目标必须是列表控件）");
        }

        /// <summary>
        /// 行点击通知：回调带 (key, 按钮节点名)——**条目根按钮 = 空串**，条目内其余按钮各回自己的节点名。
        /// 可在 SetList 前后调用（后登记 = 对已实体化条目补挂，见 <see cref="BindListSource.RebindVisible"/>）。G6。
        /// </summary>
        public void OnClick(string name, Action<string, string> handler)
        {
            if (handler == null) _click.Remove(name);
            else _click[name] = handler;
            if (_sources.TryGetValue(name, out var source)) source.RebindVisible();
        }

        /// <summary>重绑全部已实体化行（语言切换重刷行文案——行是复用条目，不做逐行订阅）。</summary>
        public void RebindVisible()
        {
            foreach (BindListSource source in _sources.Values) source.RebindVisible();
        }

        /// <summary>清除全部列表源与点击回调（<c>UnbindAll</c> 用——页面 OnShow 会重推数据）。</summary>
        public void Clear()
        {
            _click.Clear();
            foreach (BindListSource source in _sources.Values) source.Clear();
            _sources.Clear();
        }

        internal Action<string, string> HandlerFor(string listName)
            => _click.TryGetValue(listName, out var h) ? h : null;

        /// <summary>条目文本写值（数据直写；文案键解出后直写——轮询式本地化）。</summary>
        internal void WriteItem(GameObject node, in UIListField field)
        {
            string text = !string.IsNullOrEmpty(field.Text)
                ? field.Text
                : _locale.FormatOrFallback(field.Key, field.Args);
            _index.RevealOrSetText(node, text);
        }

        /// <summary>行含文案键时要求语言服务在位（缺 = 装配错误；行文案刷新走整表重绑，不做逐行订阅）。</summary>
        private void GuardLocalizedRows(string name, IUIListRows rows)
        {
            if (_locale.HasLocale) return;
            int count = rows.Count;
            for (int i = 0; i < count; i++)
            {
                IReadOnlyList<UIListField> fields = rows.FieldsAt(i);
                if (fields == null) continue;
                for (int j = 0; j < fields.Count; j++)
                    if (!string.IsNullOrEmpty(fields[j].Key))
                        throw new InvalidOperationException(
                            $"绑定索引[{name}] SetList 行含文案键但未 BindLocale（未注入语言服务）");
            }
        }

        /// <summary>
        /// 行 → 条目投影（G6）：字段按**子节点名**寻址（条目子树不设 BindName——“条目内部不暴露”）；
        /// 重绑先解除旧监听/旧 key 再写新数据（《UI框架总设计》§8.2 绑定代次纪律）。
        /// 内核（VirtualList/SimpleListView）只负责窗口与复用，内容投影全在本件。
        /// </summary>
        private sealed class BindListSource : IVirtualListSource, IVirtualListUnbind
        {
            private sealed class ClickHook
            {
                public Button Button;
                public UnityEngine.Events.UnityAction Handler;
            }

            private readonly UIBindListSource _owner;
            private readonly string _listName;
            private readonly IUIListRows _rows;
            private readonly Dictionary<Component, int> _bound = new Dictionary<Component, int>(16);
            private readonly Dictionary<Component, List<ClickHook>> _clicks = new Dictionary<Component, List<ClickHook>>(16);

            public BindListSource(UIBindListSource owner, string listName, IUIListRows rows)
            {
                _owner = owner;
                _listName = listName;
                _rows = rows;
            }

            public int Count => _rows.Count;

            public void Bind(int index, Component item)
            {
                Unbind(item);                                  // 池化复用跨行：先解除旧回调/旧键
                _bound[item] = index;

                IReadOnlyList<UIListField> fields = _rows.FieldsAt(index);
                if (fields != null)
                {
                    for (int i = 0; i < fields.Count; i++)
                    {
                        UIListField field = fields[i];
                        Transform node = FindChild(item.transform, field.Node);
                        if (node == null) continue;            // 缺节点 = 该字段不显（行协议允许留空）
                        _owner.WriteItem(node.gameObject, field);
                    }
                }

                AttachClicks(item, _rows.KeyAt(index));
            }

            public void Unbind(Component item)
            {
                if (item == null) return;
                _bound.Remove(item);
                if (_clicks.TryGetValue(item, out var hooks))
                {
                    for (int i = 0; i < hooks.Count; i++)
                        if (hooks[i].Button != null) hooks[i].Button.onClick.RemoveListener(hooks[i].Handler);
                    _clicks.Remove(item);
                }
            }

            /// <summary>清除全部已绑条目（UnbindAll 用——页面 OnShow 会重推数据）。</summary>
            public void Clear()
            {
                var items = new List<Component>(_bound.Keys);
                for (int i = 0; i < items.Count; i++) Unbind(items[i]);
            }

            /// <summary>重绑已实体化条目：语言切换重刷行文案 / 点击回调后补挂（滚动路径零开销——只碰窗口内条目）。</summary>
            public void RebindVisible()
            {
                var items = new List<KeyValuePair<Component, int>>(_bound);
                for (int i = 0; i < items.Count; i++)
                {
                    Component item = items[i].Key;
                    if (item == null) continue;
                    if (items[i].Value >= _rows.Count) { Unbind(item); continue; }
                    Bind(items[i].Value, item);
                }
            }

            private void AttachClicks(Component item, string key)
            {
                Action<string, string> handler = _owner.HandlerFor(_listName);
                if (handler == null) return;
                Button[] buttons = item.GetComponentsInChildren<Button>(true);
                if (buttons.Length == 0) return;               // 条目无按钮 = 只读行（合法）

                var hooks = new List<ClickHook>(buttons.Length);
                for (int i = 0; i < buttons.Length; i++)
                {
                    Button button = buttons[i];
                    string action = ReferenceEquals(button.transform, item.transform) ? string.Empty : button.name;
                    UnityEngine.Events.UnityAction h =
                        () => SafeCall.Invoke(() => handler(key, action), $"OnListClick[{_listName}]");
                    button.onClick.RemoveAllListeners();       // 条目池化复用：只保留本次绑定的监听
                    button.onClick.AddListener(h);
                    hooks.Add(new ClickHook { Button = button, Handler = h });
                }
                _clicks[item] = hooks;
            }

            private static Transform FindChild(Transform root, string nodeName)
            {
                if (root == null || string.IsNullOrEmpty(nodeName)) return null;
                for (int i = 0; i < root.childCount; i++)
                {
                    Transform child = root.GetChild(i);
                    if (child.name == nodeName) return child;
                    Transform deeper = FindChild(child, nodeName);
                    if (deeper != null) return deeper;
                }
                return null;
            }
        }

        private sealed class EmptyListRows : IUIListRows
        {
            public int Count => 0;
            public string KeyAt(int index) => string.Empty;
            public IReadOnlyList<UIListField> FieldsAt(int index) => Array.Empty<UIListField>();
        }
    }
}
