using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteFramework
{
    /// <summary>
    /// 模块依赖图声明（可选能力，路线图 M1）：声明本模块依赖的**其它模块名**。
    /// Host 在初始化前校验：依赖必须已注册（缺失 fail-fast）、依赖边不得成环、模块名不得重复。
    /// 未实现本接口的模块视为无声明依赖（仍按注册序初始化）。
    ///
    /// **为什么是名字而不是类型**：业务域各自一个程序集，装配段就地住进域目录——若依赖用
    /// `typeof(模块类)`，段类互相引用就会把域程序集连成环（`Modules.Input → LiteGame.Modules → Modules.Input`）。
    /// 模块身份本来就是 <see cref="IClientModule.Name"/>（Host 已按名校验唯一性），用名字声明与之一致。
    /// </summary>
    public interface IClientModuleGraph
    {
        /// <summary>依赖的模块名（与各模块 <see cref="IClientModule.Name"/> 一致；注册序之外的第二道显式声明）。</summary>
        IReadOnlyList<string> DependsOn { get; }
    }
}
