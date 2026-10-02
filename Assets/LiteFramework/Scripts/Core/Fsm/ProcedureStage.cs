using System;
using System.Threading;

namespace LiteFramework
{
    /// <summary>
    /// 流程阶段基类：**进流程即建 CTS 并跑 `RunAsync`、离场即 Cancel + Dispose**
    /// （子类只写 `RunAsync`，避免漏掉取消语义）。
    ///
    /// 构造时可传入宿主根令牌——阶段 CTS **链接**根令牌：宿主关闭时，
    /// 在途流程异步与离场取消走同一条取消链。根取消 ≠ 离场：资源释放仍由 Scope 负责。
    ///
    /// 每次迁移的数据走 <typeparamref name="TReq"/> payload，编译期强类型。
    ///
    /// `RunAsync` 是 void 签名——Core 是 S2 契约层（零第三方实现依赖），引不了 UniTask；
    /// 子类必须"一行转发"给 async 主体，**禁止 async void**（异常调用方接不住）。
    /// 流程依赖不从 payload 取（那是服务定位器的变体）——依赖走构造注入存为本类字段。
    /// </summary>
    public abstract class ProcedureStageBase<TId, TReq> : IStage<TId, TReq>
        where TId : struct
    {
        private readonly CancellationToken _rootToken;      // 宿主根令牌（default = 无根——纯 L1/工具场景）
        private CancellationTokenSource _cts;

        /// <param name="rootToken">宿主根取消令牌；传 default 保持无根语义。</param>
        protected ProcedureStageBase(CancellationToken rootToken = default)
        {
            _rootToken = rootToken;
        }

        public virtual void OnInit(IStageHost<TId, TReq> m) { }

        public void OnEnter(IStageHost<TId, TReq> m, in TReq req)
        {
            // 链接根令牌：离场取消（本阶段 CTS）与宿主关闭（根）共用一条取消链；无根时退化为独立 CTS
            _cts = _rootToken == default
                ? new CancellationTokenSource()
                : CancellationTokenSource.CreateLinkedTokenSource(_rootToken);
            RunAsync(m, in req, _cts.Token);
        }

        public void OnLeave(IStageHost<TId, TReq> m)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        public virtual void OnUpdate(IStageHost<TId, TReq> m, float elapseSeconds) { }

        protected abstract void RunAsync(IStageHost<TId, TReq> m, in TReq req, CancellationToken ct);

        /// <summary>
        /// 失败落点：记 Fatal（日志可见）。**不切流程**——错误流转由业务在 catch 里自行
        /// `m.Request(ProcedureId.Error, new ProcedureArgs(ex))`（Core 不引用业务类型）。
        /// </summary>
        protected void Fail(IStageHost<TId, TReq> m, Exception ex, string where)
        {
            Log.Fatal(ex, where, "Procedure");
        }
    }
}
