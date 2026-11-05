using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteFramework
{
    /// <summary>
    /// 受控内容缓存清理端口（恢复动作「清缓存」的实现面，《客户端依赖注入专项设计》§6.2）：
    /// 清的是**下载产物**（候选根 `config/lua/bundle`），不是内置包根；清理后激活记录回退 builtin，
    /// 下一次启动以内置内容确定可用（再按发布通道重新取候选）。
    /// </summary>
    public interface IContentCacheMaintenance
    {
        /// <summary>清空受控内容缓存并把激活记录回退 builtin（幂等）。</summary>
        UniTask<ContentCacheClearResult> ClearAsync(CancellationToken ct = default);
    }
}
