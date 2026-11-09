using System.Collections.Generic;

namespace LiteFramework.UI
{

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

        /// <summary>文案参数（≤4，模板占位参数同口径）。</summary>
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
