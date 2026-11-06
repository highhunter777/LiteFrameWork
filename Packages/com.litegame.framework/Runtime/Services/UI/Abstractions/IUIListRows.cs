using System.Collections.Generic;

namespace LiteFramework.UI
{
    /// <summary>
    /// 列表行数据口（G6，《UI控件Lua API 参考》§5"SetList"）：**C# 持数据源**——内核滚动时按位回填， 不逐帧回 Lua；行有**稳定 key**（行交互回传用，不哈希、不复用为身份）。
    /// </summary>
    public interface IUIListRows
    {
        int Count { get; }

        /// <summary>第 index 行的稳定 key（行点击回传；空串 = 无 key，页面自行忽略）。</summary>
        string KeyAt(int index);

        /// <summary>第 index 行的字段集（子节点名 → 写值指令）；空集合 = 该行条目留空。</summary>
        IReadOnlyList<UIListField> FieldsAt(int index);
    }
}
