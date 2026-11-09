using System;
using System.Collections.Generic;
using LiteFramework.UI;

namespace LiteGame.Modules.UI
{
    /// <summary>
    /// C# 行表构造：<c>Row(key).Text(节点, 值).KeyText(节点, 文案键, 参数…)</c>。
    /// 字段按条目子节点名寻址；含文案键的行随语言切换整表重绑（框架口径）。
    /// </summary>
    public sealed class UIListRowSet : IUIListRows
    {
        private readonly List<RowData> _rows = new List<RowData>(8);
        private RowData _current;

        /// <summary>新建空行表（整表替换语义：填好交给 Index.SetList）。</summary>
        public static UIListRowSet Create() => new UIListRowSet();

        public int Count => _rows.Count;

        public string KeyAt(int index) => _rows[index].Key;

        public IReadOnlyList<UIListField> FieldsAt(int index) => _rows[index].Fields;

        /// <summary>起一行（行 key = 列表稳定标识）。</summary>
        public UIListRowSet Row(string key)
        {
            _current = new RowData(key ?? string.Empty);
            _rows.Add(_current);
            return this;
        }

        /// <summary>当前行写数据文本（不随语言刷新）。</summary>
        public UIListRowSet Text(string node, string text)
        {
            Current().Fields.Add(new UIListField(node, text ?? string.Empty));
            return this;
        }

        /// <summary>当前行写本地化文案（可带参数）。</summary>
        public UIListRowSet KeyText(string node, string key, params object[] args)
        {
            Current().Fields.Add(new UIListField(node, key, args));
            return this;
        }

        private RowData Current()
            => _current ?? throw new InvalidOperationException("UIListRowSet：先 Row(key) 再写字段");

        private sealed class RowData
        {
            internal readonly string Key;
            internal readonly List<UIListField> Fields = new List<UIListField>(4);
            internal RowData(string key) => Key = key;
        }
    }
}
