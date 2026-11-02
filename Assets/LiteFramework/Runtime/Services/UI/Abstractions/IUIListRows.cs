using System.Collections.Generic;

namespace LiteFramework.UI
{
    /// <summary>
    /// 列表行数据口（G6，《UI控件Lua API 参考》§5"SetList"）：**C# 持数据源**——内核滚动时按位回填，
    /// 不逐帧回 Lua；行有**稳定 key**（行交互回传用，不哈希、不复用为身份）；行字段按
    /// **条目子节点名**寻址（条目子树不设 BindName——"条目内部不暴露"，见《上云测试页面层级设计》§0）。
    ///
    /// 语义约定：`SetList` 整表替换（更新/缩容同一入口——数据变更后再调一次即可，内核按新 Count 重算窗口）；
    /// 无专用"选择"API：选择态由页面自持（行点击 → 页面写态 → 再 SetList 或在行字段里体现）。
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
