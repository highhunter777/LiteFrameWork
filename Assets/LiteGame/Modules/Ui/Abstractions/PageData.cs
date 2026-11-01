using System;
using System.Collections.Generic;

namespace LiteGame.Modules.Ui
{
    /// <summary>
    /// 页面导航数据（中立数据面，纯数据、零引擎）：具名键 → 标量 / 子表 / 行数组，按插入序展开。
    ///
    /// 为什么需要：流程段构造页面数据（结算摘要、对局菜单门控位）时不该认识 Lua 表——
    /// 数据树在这里装配，投射成平台数据（LuaTable）由 <see cref="IPageDataFactory"/> 的实现侧负责。
    /// 取值类型只允许 bool / int / string / <see cref="PageData"/> / `IReadOnlyList&lt;PageData&gt;`（行数组）；
    /// 其余类型由实现侧 fail-fast（写错的值不静默丢字段）。
    /// </summary>
    public sealed class PageData
    {
        private readonly List<KeyValuePair<string, object>> _values =
            new List<KeyValuePair<string, object>>();

        /// <summary>具名键值对（插入序＝投射序，便于对照页面约定的表字面量）。</summary>
        public IReadOnlyList<KeyValuePair<string, object>> Values => _values;

        /// <summary>布尔值（Lua 侧 boolean）。</summary>
        public PageData Set(string key, bool value) => Add(key, value);

        /// <summary>整数值（Lua 侧 number）。</summary>
        public PageData Set(string key, int value) => Add(key, value);

        /// <summary>文本值（Lua 侧 string）。</summary>
        public PageData Set(string key, string value) => Add(key, value);

        /// <summary>子表（嵌套具名键）。</summary>
        public PageData Set(string key, PageData value) => Add(key, value);

        /// <summary>行数组（Lua 侧 1 基序数组，按列表序）。</summary>
        public PageData Set(string key, IReadOnlyList<PageData> rows) => Add(key, rows);

        /// <summary>是否没有任何键（装配点据此判定"页面无数据"）。</summary>
        public bool IsEmpty => _values.Count == 0;

        private PageData Add(string key, object value)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("页面数据键不能为空", nameof(key));
            if (value == null) throw new ArgumentNullException(nameof(value));
            // 重复键 = 装配错误：后写覆盖会让数据面静默丢字段（页面读到的不是作者写的那份）
            for (int i = 0; i < _values.Count; i++)
                if (string.Equals(_values[i].Key, key, StringComparison.Ordinal))
                    throw new InvalidOperationException("页面数据键重复：" + key);

            _values.Add(new KeyValuePair<string, object>(key, value));
            return this;
        }
    }
}
