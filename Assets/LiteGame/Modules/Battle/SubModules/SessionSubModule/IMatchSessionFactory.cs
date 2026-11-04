using System;
using LiteFramework;
using LiteFramework.Diagnostics;
using LiteFramework;
using LiteGame;
using LiteGame.Modules.Input;

namespace LiteGame
{
    /// <summary>
    /// Match 域会话工厂（《客户端依赖注入专项设计》§6）：从账号运行时端口（<see cref="IAccountRuntime"/>）
    /// 建立局内服务域（Match 子域）与 <see cref="BattleContext"/>（**类型化工厂**——
    /// 进房参数/输入服务经工厂声明依赖进域，流程不再裸 new）。
    ///
    /// **Unity 面**（BattleContext 依赖 SimView——本件只被 Unity 编译；.NET 侧验收归 DI-4）。
    /// Account 半边（<see cref="AccountSessionFactory"/>）零引擎依赖、L1 源链接直测。
    /// </summary>
    public interface IMatchSessionFactory
    {
        /// <summary>建 Match 会话：服务子域 + BattleContext（工厂产物，容器承担其释放）。
        /// 失败就地回滚（服务域 Dispose 后重抛）。<paramref name="rules"/>＝本局规则持有件（与权威侧同实例；null = 常规房）。</summary>
        MatchSession Create(IAccountRuntime account, LiteSim.MatchTestRules rules = null);
    }
}
