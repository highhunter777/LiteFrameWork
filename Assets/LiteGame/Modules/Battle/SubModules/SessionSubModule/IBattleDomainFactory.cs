using System;
using LiteFramework;
using LiteFramework.Diagnostics;
using LiteFramework;
using LiteGame;
using LiteGame.Modules.Input;
using LiteGame.Modules.Battle.Abstractions.Session;

namespace LiteGame.Modules.Battle.SubModules.SessionSubModule
{
    /// <summary>
    /// Match 域会话工厂（《客户端依赖注入专项设计》§6）：从账号运行时端口（IMatchRuntime） 建立局内服务域（Match 子域）与 BattleContext（**类型化工厂**—— 进房参数/输入服务经。）。
    /// </summary>
    public interface IBattleDomainFactory
    {
        /// <summary>建 Match 会话：服务子域 + BattleContext（工厂产物，容器承担其释放）。
        /// 失败就地回滚（服务域 Dispose 后重抛）。<paramref name="rules"/>＝本局规则持有件（与权威侧同实例；null = 常规房）。</summary>
        BattleDomain Create(IMatchRuntime account, LiteSim.MatchTestRules rules = null);
    }
}
