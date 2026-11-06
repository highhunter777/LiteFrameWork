using System;
using System.Collections;
using System.Collections.Generic;


namespace LiteFramework
{
    public interface ILuaRegistry<T>
    {
        void Fill(string name, T impl);   // C# 读注册表三件套后调用，不导出给 Lua；重复 Fill 抛
        T Get(string name);               // 未命中抛（报错信息带路径约定提示）
        bool Has(string name);
        void Clear();                     // DevReload 重填前置：旧逻辑表全弃，Generation 前进作失效纪元

        /// <summary>
        /// 失效纪元（实现见 <see cref="LuaRegistry{T}"/>）：Fill 逐项递增、Clear 非空递增。
        /// </summary>
        int Generation { get; }
    }
}
