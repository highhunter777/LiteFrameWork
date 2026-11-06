using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteFramework
{
    /// <summary>
    /// 模块作用域契约（路线图 §4.3）：模块把本次初始化创建的资源/订阅/租约登记到这里， 由 Host 在模块关闭（或启动失败的该模块回滚）时**逆序释放**。
    /// </summary>
    public interface ISegmentScope
    {
        /// <summary>作用域名（`Module:&lt;模块名&gt;`，诊断用）。</summary>
        string Name { get; }

        /// <summary>本模块的取消令牌（父级＝根作用域；宿主退出/根取消级联）。</summary>
        CancellationToken Token { get; }

        /// <summary>登记可释放资源（模块关闭时逆序释放；登记进已释放作用域即就地释放并抛）。</summary>
        void Register(IDisposable resource);
    }
}
