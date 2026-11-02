using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteFramework
{
    /// <summary>
    /// 模块关闭句柄（路线图 M1）：初始化成功后返回，Host 只经句柄关闭——把"初始化成功"与
    /// "可关闭"绑成一个对象，避免对从未初始化成功的模块调用关闭。
    /// </summary>
    public interface IClientModuleHandle
    {
        /// <summary>关闭本模块（幂等；不得抛出——异常由 Host 聚合）。</summary>
        UniTask ShutdownAsync(CancellationToken ct);
    }
}
