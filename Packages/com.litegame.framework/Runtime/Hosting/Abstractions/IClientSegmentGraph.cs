using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteFramework
{
    /// <summary>
    /// 模块依赖图声明（可选能力，路线图 M1）：声明本模块依赖的**其它模块名**。
    /// </summary>
    public interface IClientSegmentGraph
    {
        /// <summary>依赖的模块名（与各模块 <see cref="IClientSegment.Name"/> 一致；注册序之外的第二道显式声明）。</summary>
        IReadOnlyList<string> DependsOn { get; }
    }
}
