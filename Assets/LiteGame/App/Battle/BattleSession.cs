using System;
using LiteClient;
using LiteSim;

namespace LiteGame
{
    /// <summary>
    /// 对局聚合（路线图 M0 接缝 4）：Match 域的唯一持有点——持有 <see cref="MatchSession"/>
    /// （服务域 + <see cref="BattleContext"/>），并成为 `SimTestRules.ActiveMatch` 的**唯一写者**。
    ///
    /// 为什么要聚合：M0 之前 `ActiveMatch` 有三个生产写点（Seeder、BattleContext 两处），
    /// "当前局"因此是隐式全局；聚合后 BattleContext 只发布本局规则实例（`RulesBound`），
    /// 由本类登记到静态读口——读写边界收在一处，后续 M2 删除静态语义时只改这里。
    ///
    /// 生命周期：构造即接线，<see cref="Dispose"/> 先摘订阅再释放 Match 域（不留下局可读状态）。
    /// </summary>
    public sealed class BattleSession : IDisposable
    {
        private bool _disposed;

        public MatchSession Match { get; }

        /// <summary>对局上下文（网络 + Sim + 表现端口）。</summary>
        public BattleContext Context => Match.Context;

        /// <summary>本局规则实例（null = 常规房无测试规则）。</summary>
        public MatchTestRules Rules => Context.MatchRules;

        public BattleSession(MatchSession match)
        {
            Match = match ?? throw new ArgumentNullException(nameof(match));
            Context.RulesBound += PublishRules;
        }

        /// <summary>唯一写者：BattleContext 绑定/更新本局规则时登记到静态读口（调试/表现侧读它）。</summary>
        private static void PublishRules(MatchTestRules rules) => SimTestRules.ActiveMatch = rules;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Context.RulesBound -= PublishRules;
            Match.Dispose();
        }
    }
}
