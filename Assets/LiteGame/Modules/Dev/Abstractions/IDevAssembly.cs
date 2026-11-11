using System;
using LiteFramework;
using LiteFramework.UI;
using LiteFramework;
using LiteGame;
using LiteGame.Modules.Battle;
using LiteGame.Modules.Input;
using LiteNet.Transport;
using LiteGame.Modules.Battle.SubModules.CombatSubModule.Abstractions;

namespace LiteGame.Modules.Dev.Abstractions
{
    /// <summary>
    /// 开发装配端口（由 `LiteGame.Dev` 实现；产品程序集只认本接口，不引用开发程序集）。
    /// 能力面 = 会话实现 / 容器装饰（GM/弱网/调试内核装配）/ 传输包装 / 对局诊断登记 / 对局开发叠加层。
    /// </summary>
    public interface IDevAssembly
    {
        /// <summary>建开发会话（发布形态不走本接口——空实现直接 `NullDevSession`）。</summary>
        IDevSession CreateSession();

        /// <summary>本地传输工厂（测试房/进程内本地服；发布形态未实现即 null）。</summary>
        ILocalTransportFactory LocalTransport { get; }

        /// <summary>装配期装饰容器（GM 命令/弱网/调试内核）；返回的句柄负责注销与静态收口。</summary>
        IDisposable Decorate(DevServices services);

        /// <summary>包装本局传输（弱网注入装饰器）：输入测试房标记与原传输，返回装饰后的传输。
        /// **只认传输本身**（<c>IClientTransport</c>）——本局规则实例由会话域持有、不经开发面下发；
        /// 这样开发端口不认识会话域类型，两边程序集可以互不引用。</summary>
        Func<bool, IClientTransport, IClientTransport> WrapTransport { get; }

        /// <summary>对局诊断登记端口（DevHud/GM 页读口）。</summary>
        IBattleDevDiagnostics BattleDiagnostics { get; }

        /// <summary>建对局开发叠加层（爆头诊断绘制/自瞄验证；每次进局一件）。</summary>
        IBattleDevOverlay CreateBattleOverlay();

        /// <summary>注入宿主上的开发运行期调节件（DebugRuntimeTuner；未挂载 = no-op）。</summary>
        void InjectRuntimeTuners(UnityEngine.GameObject host, IWorldClock worldClock, IUIClock uiClock,
            EventBus events, CommandBus commands);
    }
}
