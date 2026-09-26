using System;
using System.Collections.Generic;
using LiteFramework;
using UnityEngine;

namespace LiteGame
{
    /// <summary>虚拟列表数据源接口（M4 §2.5 口子）：计数 + 按位刷新渲染项。C# 直实现 / Lua 经适配器实现均可。
    /// 完整虚拟化控件（复用池 / 布局回收 / 裁剪）属 M4c 控件库——本件只含接口与最小渲染验证件。</summary>
    public interface IVirtualListSource
    {
        int Count { get; }

        /// <summary>把第 index 条数据刷进渲染项（item 为池化复用件——实现方按位回填内容）。</summary>
        void Bind(int index, Component item);
    }

}
