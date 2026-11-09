using System;
using System.Collections.Generic;

namespace LiteGame.Modules.UI
{
    /// <summary>
    /// 页面导航数据（中立数据面，纯数据、零引擎）：具名键 → 标量 / 子表 / 行数组，按插入序展开。
    /// </summary>
    public sealed class PageData
    {
        private readonly List<KeyValuePair<string, object>> _values =
            new List<KeyValuePair<string, object>>();

        /// <summary>具名键值对（插入序＝投射序，便于对照页面约定的表字面量）。</summary>
        public IReadOnlyList<KeyValuePair<string, object>> Values => _values;

        /// <summary>布尔值（布尔）。</summary>
        public PageData Set(string key, bool value) => Add(key, value);

        /// <summary>整数值（整数）。</summary>
        public PageData Set(string key, int value) => Add(key, value);

        /// <summary>文本值（文本）。</summary>
        public PageData Set(string key, string value) => Add(key, value);

        /// <summary>子表（嵌套具名键）。</summary>
        public PageData Set(string key, PageData value) => Add(key, value);

        /// <summary>行数组（按列表序的数组）。</summary>
        public PageData Set(string key, IReadOnlyList<PageData> rows) => Add(key, rows);

        /// <summary>是否没有任何键（装配点据此判定"页面无数据"）。</summary>
        public bool IsEmpty => _values.Count == 0;

        private PageData Add(string key, object value)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("页面数据键不能为空", nameof(key));
            if (value == null) throw new ArgumentNullException(nameof(value));
            // 重复键 = 装配错误：后写覆盖会让数据面静默丢字段（页面读到的不是作者写的那份）。
            for (int i = 0; i < _values.Count; i++)
                if (string.Equals(_values[i].Key, key, StringComparison.Ordinal))
                    throw new InvalidOperationException("页面数据键重复：" + key);

            _values.Add(new KeyValuePair<string, object>(key, value));
            return this;
        }
    }
}
