using System;
using LiteFramework;
using LiteSim;
using LiteFramework;
using LiteGame;

namespace LiteGame
{
    /// <summary>
    /// 对局聚合（路线图 M0 接缝 4 / M2）：Match 域的唯一持有点——持有 <see cref="MatchSession"/>
    /// （服务域 + <see cref="BattleContext"/>），并把本局规则实例绑定到开发会话端口
    /// （<see cref="IDevSession.MatchRules"/>）供调试投影读取，离场解绑。
    ///
    /// 为什么要聚合：M0 之前"当前局"是隐式全局（Seeder + BattleContext 两处静态写）；聚合后
    /// 规则实例随会话显式传递、经端口暴露给 dev 投影，Sim 侧不再有静态"当前局"。
    ///
    /// 生命周期：构造即接线，<see cref="Dispose"/> 先摘订阅/解绑再释放视图域与 Match 域（不留下局可读状态）。
    ///
    /// **诊断所有权（M2）**：对局诊断读口（`BattleDiagnostics`，开发三宏内）的登记/注销归本会话——
    /// 与会话同生共死，视图域（<see cref="BattleViewRig"/>）只做装配/拆除，不再登记静态读口。
    /// </summary>
    internal sealed class BattleSession : IDisposable
    {
        private readonly IDevSession _dev;
        private bool _disposed;

        public MatchSession Match { get; }

        /// <summary>对局上下文（网络 + Sim + 表现端口）。</summary>
        public BattleContext Context => Match.Context;

        /// <summary>表现视图域（Battle 阶段创建后挂入；null = 尚未建立/已拆除）。</summary>
        public BattleViewRig View { get; private set; }

        /// <summary>本局规则实例（null = 常规房无测试规则）。</summary>
        public MatchTestRules Rules => Context.MatchRules;

        /// <summary>对局结束原因（未结束 = null）；流程只从会话读终局事实。</summary>
        public BattleContext.EndReason? EndReason => Context.EndedReason;

        /// <summary>冻结的终局结果（结算页数据源；未结束 = null）。</summary>
        public LiteNet.Proto.MatchEnded MatchResult => Context.MatchResult;

        /// <summary>本地席位（-1 = 未分配）。</summary>
        public int PlayerId => Context.PlayerId;

        public BattleSession(MatchSession match, IDevSession dev = null)
        {
            Match = match ?? throw new ArgumentNullException(nameof(match));
            _dev = dev;
            Context.RulesBound += PublishRules;
            if (Rules != null) _dev?.AttachMatchRules(Rules);   // 进房即有种子规则（StartGame 前投影也可读）
            DevHooks.Diagnostics?.Attach(Context);               // 诊断读口登记（开发装配实现；发布无实现 = 不登记）
        }

        /// <summary>挂入视图域（Battle 阶段 StartGame 后建立；生命周期随本会话，先于 Match 域释放）。</summary>
        public void AttachView(BattleViewRig view)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(BattleSession));
            View = view;
        }

        /// <summary>拆视图域（幂等）：消费者/视图/租约作用域由 Rig.Dispose 自身逆序完成。</summary>
        public void DetachView()
        {
            BattleViewRig view = View;
            View = null;
            view?.Dispose();
        }

        /// <summary>规则实例变化（StartGame/权威 GM 位）→ 绑定到 dev 端口（投影读同一实例）。</summary>
        private void PublishRules(MatchTestRules rules) => _dev?.AttachMatchRules(rules);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Context.RulesBound -= PublishRules;
            MatchTestRules rules = Rules;
            if (rules != null) _dev?.DetachMatchRules(rules);   // 离场解绑（只解绑本会话的实例）
            DevHooks.Diagnostics?.Detach(Context);               // 诊断读口注销（只注销本局实例——防误注下一局）
            DetachView();
            Match.Dispose();
        }
    }
}
