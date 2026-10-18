using System.Collections.Generic;

namespace LiteClient.UI
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

    /// <summary>行字段写值指令：<c>Text</c> = 数据直写（不随语言刷新）；<c>Key</c>(+Args) = 本地化文案
    /// （语言切换时列表**整表重绑**刷新——行是复用条目，不做逐行订阅）。二者互斥（Text 非空优先）。</summary>
    public readonly struct UIListField
    {
        /// <summary>条目内子节点名（如 Txt_RoomName）。</summary>
        public readonly string Node;

        /// <summary>数据文本（SetText 口径；可空）。</summary>
        public readonly string Text;

        /// <summary>文案键（SetTextKeyArgs 口径；可空）。</summary>
        public readonly string Key;

        /// <summary>文案参数（≤4，与 Lua 面 arg0..arg3 同口径）。</summary>
        public readonly object[] Args;

        public UIListField(string node, string text)
        {
            Node = node;
            Text = text;
            Key = null;
            Args = null;
        }

        public UIListField(string node, string key, object[] args)
        {
            Node = node;
            Text = null;
            Key = key;
            Args = args;
        }
    }
}
